using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SoloCrm.Application.Features.Webhooks;

namespace SoloCrm.Infrastructure.Webhooks;

/// <summary>
/// Posts the webhook payload (SPEC 5) with signature headers. Redirects are not followed and requests time out
/// after <see cref="Timeout"/>; both count as failed attempts. Logs contain neither body, secret nor URL (SPEC 6).
/// </summary>
internal sealed partial class HttpWebhookSender(IHttpClientFactory httpClientFactory, TimeProvider timeProvider, ILogger<HttpWebhookSender> logger)
    : IWebhookSender
{
    public const string ClientName = "webhooks";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task<WebhookSendResult> SendAsync(string url, string secret, WebhookEvent webhookEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(webhookEvent);

        var body = Serialize(webhookEvent);
        var timestamp = timeProvider.GetUtcNow().ToUnixTimeSeconds();

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        request.Headers.Add(WebhookSignature.TimestampHeader, timestamp.ToString(CultureInfo.InvariantCulture));
        request.Headers.Add(WebhookSignature.SignatureHeader, WebhookSignature.Compute(secret, timestamp, body));
        request.Headers.Add(WebhookSignature.EventHeader, webhookEvent.Type);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("SoloCRM-Webhooks", "1.0"));

        var started = timeProvider.GetTimestamp();
        WebhookSendResult result;
        try
        {
            var client = httpClientFactory.CreateClient(ClientName);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var status = (int)response.StatusCode;
            result = new WebhookSendResult(status, ElapsedMs(started), response.IsSuccessStatusCode ? null : $"HTTP {status}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result = new WebhookSendResult(null, ElapsedMs(started), $"Zeitüberschreitung nach {Timeout.TotalSeconds:0} s");
        }
        catch (HttpRequestException ex)
        {
            result = new WebhookSendResult(null, ElapsedMs(started), $"Verbindungsfehler ({ex.HttpRequestError})");
        }

        if (result.Succeeded)
        {
            LogSent(logger, webhookEvent.Type, webhookEvent.Id, result.StatusCode, result.DurationMs);
        }
        else
        {
            LogFailed(logger, webhookEvent.Type, webhookEvent.Id, result.StatusCode, result.DurationMs, result.Error);
        }

        return result;
    }

    /// <summary><c>{ "id", "type", "occurredAt", "data" }</c>; <c>data</c> is the stored event JSON as is.</summary>
    public static string Serialize(WebhookEvent webhookEvent)
    {
        ArgumentNullException.ThrowIfNull(webhookEvent);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("id", webhookEvent.Id);
            writer.WriteString("type", webhookEvent.Type);
            writer.WriteString("occurredAt", webhookEvent.OccurredAt.UtcDateTime);
            writer.WritePropertyName("data");
            writer.WriteRawValue(webhookEvent.DataJson);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private int ElapsedMs(long started) => (int)Math.Round(timeProvider.GetElapsedTime(started).TotalMilliseconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "Webhook {EventType} {WebhookEventId} delivered: HTTP {StatusCode} in {DurationMs} ms")]
    private static partial void LogSent(ILogger logger, string eventType, Guid webhookEventId, int? statusCode, int durationMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook {EventType} {WebhookEventId} failed: {StatusCode} in {DurationMs} ms ({Error})")]
    private static partial void LogFailed(ILogger logger, string eventType, Guid webhookEventId, int? statusCode, int durationMs, string? error);
}
