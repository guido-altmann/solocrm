using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Mail;
using SoloCrm.Infrastructure.Persistence;

namespace SoloCrm.Infrastructure.Mail;

/// <summary>Provides access tokens for Microsoft Graph.</summary>
internal interface IGraphAccessTokenProvider
{
    /// <exception cref="MailSourceException">No mailbox connected, or the sign-in expired.</exception>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Sign-in to Microsoft 365 with the authorization code flow and PKCE; the refresh token is stored encrypted with
/// Data Protection (ADR-013). Access tokens are cached in memory until shortly before they expire.
/// </summary>
internal sealed partial class GraphAuthenticator(
    IHttpClientFactory httpClientFactory,
    IDbContextFactory<CrmDbContext> dbFactory,
    ISecretProtector protector,
    IOptions<GraphMailOptions> options,
    TimeProvider timeProvider,
    ILogger<GraphAuthenticator> logger) : IMailConnector, IGraphAccessTokenProvider, IDisposable
{
    public const string ClientName = "graph";
    public const string Provider = "Graph";
    public const string Scope = "offline_access Mail.Read User.Read";
    public const string GraphBase = "https://graph.microsoft.com/v1.0";

    private const string AuthorityHost = "https://login.microsoftonline.com";
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(1);

    private readonly GraphMailOptions _options = options.Value;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public bool IsConfigured => _options.IsConfigured && TenantPattern().IsMatch(_options.TenantId!);

    public Uri BuildAuthorizationUri(string redirectUri, string state, string codeChallenge)
    {
        EnsureConfigured();
        var query = string.Join('&',
            $"client_id={Uri.EscapeDataString(_options.ClientId!)}",
            "response_type=code",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
            "response_mode=query",
            $"scope={Uri.EscapeDataString(Scope)}",
            $"state={Uri.EscapeDataString(state)}",
            $"code_challenge={Uri.EscapeDataString(codeChallenge)}",
            "code_challenge_method=S256",
            "prompt=select_account");
        return new Uri($"{AuthorityHost}/{_options.TenantId}/oauth2/v2.0/authorize?{query}");
    }

    public async Task<string> ConnectAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var tokens = await RequestTokensAsync(
            [
                ("grant_type", "authorization_code"),
                ("code", code),
                ("code_verifier", codeVerifier),
                ("redirect_uri", redirectUri),
            ],
            cancellationToken);
        if (tokens.RefreshToken is null)
        {
            throw new MailSourceException("Microsoft hat keinen Zugang für den dauerhaften Abgleich geliefert (offline_access fehlt).");
        }

        var account = await ReadAccountAsync(tokens.AccessToken, cancellationToken);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var connection = await db.MailConnections.SingleOrDefaultAsync(c => c.Provider == Provider, cancellationToken);
        var protectedToken = protector.Protect(tokens.RefreshToken);
        var now = timeProvider.GetUtcNow();
        if (connection is null)
        {
            db.MailConnections.Add(new MailConnection(Provider, account, protectedToken, now));
        }
        else
        {
            if (!string.Equals(connection.Account, account, StringComparison.OrdinalIgnoreCase))
            {
                // The progress belongs to the other mailbox.
                await db.MailSyncStates.Where(s => s.Provider == Provider).ExecuteDeleteAsync(cancellationToken);
            }

            connection.Reconnect(account, protectedToken, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        Cache(tokens);
        return account;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.MailConnections.Where(c => c.Provider == Provider).ExecuteDeleteAsync(cancellationToken);
        await db.MailSyncStates.Where(s => s.Provider == Provider).ExecuteDeleteAsync(cancellationToken);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            _accessToken = null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<MailConnectionInfo?> GetConnectionAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.MailConnections
            .AsNoTracking()
            .Where(c => c.Provider == Provider)
            .Select(c => new MailConnectionInfo(c.Provider, c.Account, c.ConnectedAt, c.LastSyncedAt, c.LastError))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task RecordSyncAsync(string? error, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var connection = await db.MailConnections.SingleOrDefaultAsync(c => c.Provider == Provider, cancellationToken);
        if (connection is null)
        {
            return;
        }

        if (error is null)
        {
            connection.RecordSuccess(timeProvider.GetUtcNow());
        }
        else
        {
            connection.RecordFailure(error);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && timeProvider.GetUtcNow() < _accessTokenExpiresAt - ExpiryMargin)
            {
                return _accessToken;
            }

            EnsureConfigured();
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var connection = await db.MailConnections.SingleOrDefaultAsync(c => c.Provider == Provider, cancellationToken)
                ?? throw new MailSourceException("Es ist kein Postfach verbunden.");

            string refreshToken;
            try
            {
                refreshToken = protector.Unprotect(connection.ProtectedRefreshToken);
            }
            catch (CryptographicException)
            {
                throw new MailSourceException("Der gespeicherte Zugang ist nicht lesbar (Schlüssel gewechselt). Bitte das Postfach neu verbinden.");
            }

            var tokens = await RequestTokensAsync([("grant_type", "refresh_token"), ("refresh_token", refreshToken)], cancellationToken);
            if (tokens.RefreshToken is not null && tokens.RefreshToken != refreshToken)
            {
                connection.ReplaceRefreshToken(protector.Protect(tokens.RefreshToken));
                await db.SaveChangesAsync(cancellationToken);
            }

            Cache(tokens);
            return tokens.AccessToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    private void Cache(Tokens tokens)
    {
        _accessToken = tokens.AccessToken;
        _accessTokenExpiresAt = timeProvider.GetUtcNow().AddSeconds(tokens.ExpiresInSeconds);
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("The Microsoft 365 mailbox is not configured (Mail:Graph).");
        }
    }

    private async Task<Tokens> RequestTokensAsync(IEnumerable<(string Key, string Value)> parameters, CancellationToken cancellationToken)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("client_id", _options.ClientId!),
            new("client_secret", _options.ClientSecret!),
            new("scope", Scope),
        };
        form.AddRange(parameters.Select(p => new KeyValuePair<string, string>(p.Key, p.Value)));

        try
        {
            using var client = httpClientFactory.CreateClient(ClientName);
            using var response = await client.PostAsync(
                new Uri($"{AuthorityHost}/{_options.TenantId}/oauth2/v2.0/token"), new FormUrlEncodedContent(form), cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (response.IsSuccessStatusCode && root.TryGetProperty("access_token", out var access))
            {
                return new Tokens(
                    access.GetString()!,
                    root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null,
                    root.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var seconds) ? seconds : 300);
            }

            var error = root.TryGetProperty("error", out var code) ? code.GetString() : null;
            LogTokenRequestRejected(logger, (int)response.StatusCode, error);
            throw error is "invalid_grant" or "interaction_required"
                ? new MailSourceException("Die Anmeldung bei Microsoft ist abgelaufen oder wurde widerrufen. Bitte das Postfach neu verbinden.")
                : new MailSourceException($"Die Anmeldung bei Microsoft wurde abgelehnt ({error ?? "HTTP " + (int)response.StatusCode}). Bitte Client-Id, Mandant und Client-Secret prüfen.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
        {
            throw new MailSourceException("Die Anmeldung bei Microsoft ist gerade nicht möglich.", ex);
        }
    }

    private async Task<string> ReadAccountAsync(string accessToken, CancellationToken cancellationToken)
    {
        try
        {
            using var client = httpClientFactory.CreateClient(ClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{GraphBase}/me?$select=mail,userPrincipalName"));
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new MailSourceException("Microsoft verweigert den Zugriff auf das Postfach. Bitte Berechtigungen und Zustimmung in Entra prüfen.");
            }

            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            var mail = root.TryGetProperty("mail", out var m) ? m.GetString() : null;
            var upn = root.TryGetProperty("userPrincipalName", out var u) ? u.GetString() : null;
            return !string.IsNullOrWhiteSpace(mail) ? mail : !string.IsNullOrWhiteSpace(upn) ? upn : throw new MailSourceException("Das Konto hat keine Mailbox-Adresse.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
        {
            throw new MailSourceException("Das Konto konnte nicht von Microsoft gelesen werden.", ex);
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.\-]*$")]
    private static partial Regex TenantPattern();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Microsoft token request rejected: HTTP {StatusCode}, error {Error}")]
    private static partial void LogTokenRequestRejected(ILogger logger, int statusCode, string? error);

    private sealed record Tokens(string AccessToken, string? RefreshToken, int ExpiresInSeconds);
}
