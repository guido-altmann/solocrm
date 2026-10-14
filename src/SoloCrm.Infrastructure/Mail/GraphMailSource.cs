using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Mail;

namespace SoloCrm.Infrastructure.Mail;

/// <summary>
/// Reads Inbox and Sent of the connected Microsoft 365 mailbox through Microsoft Graph delta queries (ADR-013).
/// Read-only (<c>Mail.Read</c>). Follows only links on <c>graph.microsoft.com</c>, so the access token is never sent
/// elsewhere. Neither URLs nor message content are logged.
/// </summary>
internal sealed partial class GraphMailSource(
    IHttpClientFactory httpClientFactory,
    IGraphAccessTokenProvider tokens,
    ILogger<GraphMailSource> logger) : IMailSource
{
    public const int PageSize = 50;

    private const string GraphHost = "graph.microsoft.com";
    private const string Fields = "id,internetMessageId,subject,from,toRecipients,ccRecipients,receivedDateTime,sentDateTime,body,isDraft";

    public string Provider => GraphAuthenticator.Provider;

    public async Task<MailBatch> GetChangesAsync(MailFolder folder, string? continuation, DateTimeOffset? receivedSince, CancellationToken cancellationToken)
    {
        var uri = continuation is null ? InitialUri(folder, receivedSince) : ContinuationUri(continuation);
        var accessToken = await tokens.GetAccessTokenAsync(cancellationToken);

        string json;
        try
        {
            using var client = httpClientFactory.CreateClient(GraphAuthenticator.ClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.TryAddWithoutValidation("Prefer", $"odata.maxpagesize={PageSize}");
            using var response = await client.SendAsync(request, cancellationToken);
            json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw ToException(response.StatusCode, ErrorCode(json), continuation is not null);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new MailSourceException("Microsoft Graph ist nicht erreichbar.", ex);
        }

        try
        {
            return Parse(json, folder);
        }
        catch (JsonException ex)
        {
            LogUnreadableResponse(logger);
            throw new MailSourceException("Die Antwort von Microsoft Graph war nicht lesbar.", ex);
        }
    }

    private static Uri InitialUri(MailFolder folder, DateTimeOffset? receivedSince)
    {
        var folderName = folder == MailFolder.Sent ? "sentitems" : "inbox";
        var uri = $"{GraphAuthenticator.GraphBase}/me/mailFolders/{folderName}/messages/delta?$select={Fields}";
        if (receivedSince is { } since)
        {
            var value = since.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            uri += $"&$filter=receivedDateTime%20ge%20{Uri.EscapeDataString(value)}";
        }

        return new Uri(uri);
    }

    private static Uri ContinuationUri(string continuation) =>
        Uri.TryCreate(continuation, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host == GraphHost && uri.Port == 443
            ? uri
            : throw new MailDeltaExpiredException("Der gespeicherte Fortsetzungspunkt ist ungültig.");

    private static string? ErrorCode(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("code", out var code)
                ? code.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static MailSourceException ToException(HttpStatusCode status, string? code, bool isContinuation) => status switch
    {
        HttpStatusCode.Gone => new MailDeltaExpiredException(),
        HttpStatusCode.BadRequest when isContinuation && code is "syncStateNotFound" or "SyncStateNotFound" or "resyncRequired" or "syncStateInvalid" => new MailDeltaExpiredException(),
        HttpStatusCode.Unauthorized => new MailSourceException("Microsoft hat den Zugriff abgelehnt (401). Bitte das Postfach neu verbinden."),
        HttpStatusCode.Forbidden => new MailSourceException("Microsoft verweigert den Zugriff (403). Bitte die Berechtigung Mail.Read und die Zustimmung in Entra prüfen."),
        HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout or HttpStatusCode.BadGateway =>
            new MailSourceException($"Microsoft Graph ist vorübergehend nicht verfügbar oder drosselt (HTTP {(int)status}). Der nächste Abgleich versucht es erneut."),
        _ => new MailSourceException($"Microsoft Graph antwortete mit HTTP {(int)status}."),
    };

    private static MailBatch Parse(string json, MailFolder folder)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var messages = new List<IncomingMail>();
        if (root.TryGetProperty("value", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("@removed", out _) || Bool(item, "isDraft"))
                {
                    continue;
                }

                if (ToMail(item, folder) is { } mail)
                {
                    messages.Add(mail);
                }
            }
        }

        return new MailBatch(messages, String(root, "@odata.nextLink"), String(root, "@odata.deltaLink"));
    }

    private static IncomingMail? ToMail(JsonElement item, MailFolder folder)
    {
        var id = String(item, "id");
        var received = Date(item, "receivedDateTime");
        var sent = Date(item, "sentDateTime");
        var occurredAt = folder == MailFolder.Sent ? sent ?? received : received ?? sent;
        if (id is null || occurredAt is null)
        {
            return null;
        }

        var body = item.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.Object ? b : (JsonElement?)null;
        return new IncomingMail(
            id,
            String(item, "internetMessageId"),
            folder,
            item.TryGetProperty("from", out var from) ? Address(from) : null,
            [.. Addresses(item, "toRecipients"), .. Addresses(item, "ccRecipients")],
            String(item, "subject"),
            occurredAt.Value,
            body is { } content ? String(content, "content") : null,
            body is { } typed && string.Equals(String(typed, "contentType"), "html", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> Addresses(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var list) || list.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var recipient in list.EnumerateArray())
        {
            if (Address(recipient) is { } address)
            {
                yield return address;
            }
        }
    }

    /// <summary>The plain address; Exchange-internal addresses (legacy X500 distinguished names) have no "@" and are ignored.</summary>
    private static string? Address(JsonElement person) =>
        person.ValueKind == JsonValueKind.Object
        && person.TryGetProperty("emailAddress", out var email)
        && String(email, "address") is { } address
        && address.Contains('@', StringComparison.Ordinal)
            ? address.Trim()
            : null;

    private static string? String(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Bool(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static DateTimeOffset? Date(JsonElement element, string property) =>
        String(element, property) is { } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date.ToUniversalTime()
            : null;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Microsoft Graph returned a response that could not be read")]
    private static partial void LogUnreadableResponse(ILogger logger);
}
