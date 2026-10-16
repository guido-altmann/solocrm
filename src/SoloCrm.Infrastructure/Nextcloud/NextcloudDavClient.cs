using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Infrastructure.Nextcloud;

/// <summary>
/// CardDAV/CalDAV over plain <see cref="HttpClient"/> (ADR-014). Credentials go only to the origin of the server URL:
/// redirects are followed manually and only within that origin. The log contains methods and status codes only.
/// </summary>
internal sealed partial class NextcloudDavClient(IHttpClientFactory httpClientFactory, ILogger<NextcloudDavClient> logger)
    : IDavSyncTarget
{
    public const string ClientName = "nextcloud";
    public const string CollectionDisplayName = "SoloCRM";

    /// <summary>Path segments tried for a new collection when the first one is taken.</summary>
    private static readonly string[] CollectionSlugs = ["solocrm", "solocrm-2", "solocrm-3", "solocrm-4", "solocrm-5"];

    private const int MaxRedirects = 5;

    private static readonly HttpMethod PropFindMethod = new("PROPFIND");
    private static readonly HttpMethod MkColMethod = new("MKCOL");
    private static readonly HttpMethod MkCalendarMethod = new("MKCALENDAR");
    private static readonly HttpMethod ReportMethod = new("REPORT");

    public async Task<string> EnsureAddressBookAsync(DavAccount account, CancellationToken cancellationToken)
    {
        var principal = await FindPrincipalAsync(account, cancellationToken);
        var home = await FindHomeSetAsync(account, principal, Dav.CardDav + "addressbook-home-set", cancellationToken)
            ?? throw new DavException("Der Server bietet kein Adressbuch (CardDAV) an.");

        var collections = await PropFindAsync(account, home, depth: 1, cancellationToken, Dav.D + "resourcetype", Dav.D + "displayname");
        var existing = collections.FirstOrDefault(c =>
            c.HasResourceType(Dav.CardDav + "addressbook") && c.Text(Dav.D + "displayname") == CollectionDisplayName);
        if (existing is not null)
        {
            return existing.Href;
        }

        return await CreateCollectionAsync(account, home, MkColMethod, Dav.MkAddressBook(CollectionDisplayName), cancellationToken);
    }

    public async Task<string> EnsureTaskListAsync(DavAccount account, CancellationToken cancellationToken)
    {
        var principal = await FindPrincipalAsync(account, cancellationToken);
        var home = await FindHomeSetAsync(account, principal, Dav.CalDav + "calendar-home-set", cancellationToken)
            ?? throw new DavException("Der Server bietet keine Kalender und Aufgaben (CalDAV) an.");

        var collections = await PropFindAsync(
            account, home, depth: 1, cancellationToken, Dav.D + "resourcetype", Dav.D + "displayname", Dav.CalDav + "supported-calendar-component-set");
        var existing = collections.FirstOrDefault(c =>
            c.HasResourceType(Dav.CalDav + "calendar") && c.Text(Dav.D + "displayname") == CollectionDisplayName && SupportsTasks(c));
        if (existing is not null)
        {
            return existing.Href;
        }

        return await CreateCollectionAsync(account, home, MkCalendarMethod, Dav.MkTaskList(CollectionDisplayName), cancellationToken);
    }

    public async Task<DavChanges> GetChangesAsync(DavAccount account, string collectionHref, string? syncToken, CancellationToken cancellationToken)
    {
        var body = Dav.SyncCollection(syncToken);
        using var response = await SendAsync(account, ReportMethod, collectionHref, () => XmlContent(body), r => r.Headers.Add("Depth", "0"), cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode != HttpStatusCode.MultiStatus)
        {
            // RFC 6578 3.2: an unknown or expired token is answered with the precondition valid-sync-token (Nextcloud: 403).
            if (syncToken is not null && content.Contains("valid-sync-token", StringComparison.Ordinal))
            {
                throw new DavSyncTokenExpiredException();
            }

            EnsureSuccess(response, "REPORT");
            throw new DavException("Nextcloud hat unerwartet geantwortet.");
        }

        try
        {
            var (responses, newToken) = DavMultiStatus.ParseWithSyncToken(content);
            var collectionPath = DavMultiStatus.ToPath(collectionHref);
            var changes = responses
                .Where(r => !string.Equals(r.Href.TrimEnd('/'), collectionPath.TrimEnd('/'), StringComparison.Ordinal))
                .Select(r => new DavChange(r.Href, r.Text(Dav.D + "getetag"), r.Status == (int)HttpStatusCode.NotFound))
                .ToList();
            return new DavChanges(changes, newToken);
        }
        catch (FormatException ex)
        {
            throw new DavException("Nextcloud hat eine unlesbare Antwort geschickt.", ex);
        }
    }

    public async Task<string?> GetAsync(DavAccount account, string href, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(account, HttpMethod.Get, href, body: null, configure: null, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return null;
        }

        EnsureSuccess(response, "GET");
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<DavWriteResult> PutAsync(
        DavAccount account, string href, DavContent content, string? ifMatch, bool unconditional, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(account, HttpMethod.Put, href, () =>
        {
            var body = new StringContent(content.Body, Encoding.UTF8);
            body.Headers.ContentType = MediaTypeHeaderValue.Parse(content.MediaType);
            return body;
        }, request =>
        {
            if (unconditional)
            {
                return;
            }

            if (ifMatch is null)
            {
                request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Any);
            }
            else
            {
                request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
            }
        }, cancellationToken);

        if (response.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            return DavWriteResult.Conflict;
        }

        EnsureSuccess(response, "PUT");
        var etag = ETagOf(response) ?? await ReadETagAsync(account, href, cancellationToken);
        return DavWriteResult.Written(etag);
    }

    public async Task DeleteAsync(DavAccount account, string href, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(account, HttpMethod.Delete, href, body: null, configure: null, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            EnsureSuccess(response, "DELETE");
        }
    }

    /// <summary>
    /// <c>current-user-principal</c> of the account. Tried on the URL as entered, on the Nextcloud DAV root and on the
    /// well-known address (RFC 6764), because users enter the plain server address.
    /// </summary>
    private async Task<string> FindPrincipalAsync(DavAccount account, CancellationToken cancellationToken)
    {
        var server = account.ServerUrl.AbsoluteUri.TrimEnd('/');
        string[] candidates = [server + "/", server + "/remote.php/dav/", new Uri(account.ServerUrl, "/.well-known/carddav").AbsoluteUri];
        foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
        {
            IReadOnlyList<DavResponse> responses;
            try
            {
                responses = await PropFindAsync(account, candidate, depth: 0, cancellationToken, Dav.D + "current-user-principal");
            }
            catch (DavException ex) when (ex is not DavUnauthorizedException and not DavUnreachableException)
            {
                continue;
            }

            if (responses.Select(r => r.HrefIn(Dav.D + "current-user-principal")).FirstOrDefault(h => h is not null) is { } principal)
            {
                return principal;
            }
        }

        throw new DavException("Unter dieser Adresse wurde keine Nextcloud (CardDAV/CalDAV) gefunden.");
    }

    private async Task<string?> FindHomeSetAsync(DavAccount account, string principal, XName homeSet, CancellationToken cancellationToken)
    {
        var responses = await PropFindAsync(account, principal, depth: 0, cancellationToken, homeSet);
        return responses.Select(r => r.HrefIn(homeSet)).FirstOrDefault(h => h is not null) is { } href
            ? DavMultiStatus.ToPath(href)
            : null;
    }

    private async Task<string> CreateCollectionAsync(DavAccount account, string home, HttpMethod method, string body, CancellationToken cancellationToken)
    {
        foreach (var slug in CollectionSlugs)
        {
            var href = (home.EndsWith('/') ? home : home + "/") + slug + "/";
            using var response = await SendAsync(account, method, href, () => XmlContent(body), configure: null, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Created)
            {
                return href;
            }

            // Nextcloud answers 405, Radicale 403 when the path is taken by another collection.
            if (response.StatusCode is not (HttpStatusCode.MethodNotAllowed or HttpStatusCode.Forbidden or HttpStatusCode.Conflict))
            {
                EnsureSuccess(response, method.Method);
            }
        }

        throw new DavException("Die Sammlung „SoloCRM“ konnte in Nextcloud nicht angelegt werden.");
    }

    private async Task<string?> ReadETagAsync(DavAccount account, string href, CancellationToken cancellationToken)
    {
        var responses = await PropFindAsync(account, href, depth: 0, cancellationToken, Dav.D + "getetag");
        return responses.Select(r => r.Text(Dav.D + "getetag")).FirstOrDefault(e => e is not null);
    }

    private async Task<IReadOnlyList<DavResponse>> PropFindAsync(
        DavAccount account, string target, int depth, CancellationToken cancellationToken, params XName[] properties)
    {
        var body = Dav.PropFind(properties);
        using var response = await SendAsync(
            account, PropFindMethod, target, () => XmlContent(body), r => r.Headers.Add("Depth", depth.ToString(System.Globalization.CultureInfo.InvariantCulture)), cancellationToken);
        if (response.StatusCode != HttpStatusCode.MultiStatus)
        {
            EnsureSuccess(response, "PROPFIND");
            throw new DavException("Nextcloud hat unerwartet geantwortet.");
        }

        try
        {
            return DavMultiStatus.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (FormatException ex)
        {
            throw new DavException("Nextcloud hat eine unlesbare Antwort geschickt.", ex);
        }
    }

    /// <summary>
    /// Sends a request with Basic authentication. Redirects are followed only within the origin of the server URL, so
    /// the credentials never reach another host. <paramref name="body"/> is created per attempt because content
    /// cannot be sent twice.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(
        DavAccount account,
        HttpMethod method,
        string target,
        Func<HttpContent>? body,
        Action<HttpRequestMessage>? configure,
        CancellationToken cancellationToken)
    {
        var uri = Resolve(account, target);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{account.UserName}:{account.Password}"));
        using var client = httpClientFactory.CreateClient(ClientName);

        for (var redirects = 0; ; redirects++)
        {
            using var request = new HttpRequestMessage(method, uri) { Content = body?.Invoke() };
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            configure?.Invoke(request);

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                LogRequestFailed(logger, method.Method, ex.GetType().Name);
                throw new DavUnreachableException(ex);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                LogRejected(logger, method.Method, 401);
                throw new DavUnauthorizedException();
            }

            if (!IsRedirect(response.StatusCode) || response.Headers.Location is not { } location)
            {
                return response;
            }

            response.Dispose();
            var next = location.IsAbsoluteUri ? location : new Uri(uri, location);
            if (!IsSameOrigin(next, account.ServerUrl) || redirects >= MaxRedirects)
            {
                LogRejected(logger, method.Method, (int)response.StatusCode);
                throw new DavException("Nextcloud leitet auf eine andere Adresse um. Bitte die Server-Adresse prüfen.");
            }

            uri = next;
        }
    }

    /// <summary>Without <c>supported-calendar-component-set</c> a calendar accepts every component (RFC 4791 5.2.3).</summary>
    private static bool SupportsTasks(DavResponse collection) =>
        collection[Dav.CalDav + "supported-calendar-component-set"] is not { } set
        || set.Elements(Dav.CalDav + "comp").Any(c => (string?)c.Attribute("name") == "VTODO");

    private static Uri Resolve(DavAccount account, string target) =>
        Uri.TryCreate(target, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https"
            ? IsSameOrigin(absolute, account.ServerUrl)
                ? absolute
                : throw new DavException("Nextcloud verweist auf eine andere Adresse. Bitte die Server-Adresse prüfen.")
            : new Uri(account.ServerUrl, target);

    private static bool IsSameOrigin(Uri uri, Uri server) =>
        Uri.Compare(uri, server, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static string? ETagOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("ETag", out var values) ? values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) : null;

    private static StringContent XmlContent(string body) => new(body, Encoding.UTF8, "application/xml");

    private void EnsureSuccess(HttpResponseMessage response, string method)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var status = (int)response.StatusCode;
        LogRejected(logger, method, status);
        throw status switch
        {
            403 => new DavException("Nextcloud verweigert den Zugriff (HTTP 403). Bitte die Berechtigungen des App-Passworts prüfen."),
            404 => new DavException("Nextcloud hat die Sammlung „SoloCRM“ nicht gefunden (HTTP 404). Bitte die Zugangsdaten neu speichern."),
            507 => new DavException("Der Speicherplatz in Nextcloud ist voll (HTTP 507)."),
            >= 500 => new DavUnreachableException($"Nextcloud hat einen Serverfehler gemeldet (HTTP {status})."),
            _ => new DavException($"Nextcloud hat die Anfrage abgelehnt (HTTP {status})."),
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Nextcloud {Method} answered HTTP {StatusCode}")]
    private static partial void LogRejected(ILogger logger, string method, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Nextcloud {Method} failed: {ErrorType}")]
    private static partial void LogRequestFailed(ILogger logger, string method, string errorType);
}

/// <summary>Network failure or server error; the next run tries again.</summary>
internal sealed class DavUnreachableException : DavException
{
    public DavUnreachableException()
        : base("Nextcloud ist gerade nicht erreichbar.")
    {
    }

    public DavUnreachableException(string message)
        : base(message)
    {
    }

    public DavUnreachableException(Exception innerException)
        : base("Nextcloud ist gerade nicht erreichbar.", innerException)
    {
    }

    public DavUnreachableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
