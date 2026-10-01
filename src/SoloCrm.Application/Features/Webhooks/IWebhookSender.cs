namespace SoloCrm.Application.Features.Webhooks;

/// <summary>
/// Sends one signed webhook request (SPEC 5). Implemented in Infrastructure with <c>IHttpClientFactory</c>;
/// used by the outbox processor and by „Test senden“.
/// </summary>
public interface IWebhookSender
{
    /// <param name="url">Target URL of the subscription.</param>
    /// <param name="secret">Plaintext signing secret.</param>
    /// <param name="webhookEvent">Event id, public type, time and the <c>data</c> object as JSON.</param>
    /// <param name="cancellationToken">Cancels the request; a timeout is reported as a failed attempt.</param>
    Task<WebhookSendResult> SendAsync(string url, string secret, WebhookEvent webhookEvent, CancellationToken cancellationToken);
}

/// <summary>The envelope of a webhook payload; <see cref="DataJson"/> holds only ids and status (decision 2).</summary>
public sealed record WebhookEvent(Guid Id, string Type, DateTimeOffset OccurredAt, string DataJson);

/// <summary>
/// Outcome of a single request. <see cref="StatusCode"/> is <c>null</c> when no response arrived.
/// <see cref="Error"/> is a short technical reason without response bodies (SPEC 6).
/// </summary>
public sealed record WebhookSendResult(int? StatusCode, int DurationMs, string? Error)
{
    public bool Succeeded => Error is null && StatusCode is >= 200 and < 300;
}
