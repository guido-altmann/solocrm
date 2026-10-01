namespace SoloCrm.Domain.Webhooks;

/// <summary>
/// One delivery attempt of an event to a subscription (SPEC 2.4, US-18 AK3). Contains neither the body nor the
/// secret (SPEC 6); kept for 30 days (iteration 5 decision 8).
/// </summary>
public sealed class WebhookDelivery
{
    public const int EventTypeMaxLength = 100;
    public const int ErrorMaxLength = 500;

    // Required by EF Core.
    private WebhookDelivery()
    {
        EventType = null!;
    }

    private WebhookDelivery(
        Guid subscriptionId, Guid eventId, string eventType, int attempt, int? statusCode, int durationMs, string? error, DateTimeOffset attemptedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);

        Id = Guid.CreateVersion7();
        SubscriptionId = subscriptionId;
        EventId = eventId;
        EventType = eventType;
        Attempt = attempt;
        StatusCode = statusCode;
        DurationMs = Math.Max(0, durationMs);
        Error = error is { Length: > ErrorMaxLength } ? error[..ErrorMaxLength] : error;
        AttemptedAt = attemptedAt;
        Succeeded = error is null && statusCode is >= 200 and < 300;
    }

    public Guid Id { get; private set; }

    public Guid SubscriptionId { get; private set; }

    /// <summary>The event id from the payload: the outbox message id, or a fresh id for a ping.</summary>
    public Guid EventId { get; private set; }

    public string EventType { get; private set; }

    /// <summary>1-based attempt number for this subscription and event.</summary>
    public int Attempt { get; private set; }

    /// <summary>HTTP status code; <c>null</c> when no response arrived (timeout, connection error).</summary>
    public int? StatusCode { get; private set; }

    public int DurationMs { get; private set; }

    /// <summary>Short technical reason for a failure, without response bodies.</summary>
    public string? Error { get; private set; }

    public DateTimeOffset AttemptedAt { get; private set; }

    /// <summary>HTTP 2xx (US-18).</summary>
    public bool Succeeded { get; private set; }

    public static WebhookDelivery Record(
        Guid subscriptionId, Guid eventId, string eventType, int attempt, int? statusCode, int durationMs, string? error, DateTimeOffset attemptedAt) =>
        new(subscriptionId, eventId, eventType, attempt, statusCode, durationMs, error, attemptedAt);
}
