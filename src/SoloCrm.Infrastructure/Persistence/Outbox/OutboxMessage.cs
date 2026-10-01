using System.Text.Json;
using SoloCrm.Domain.Common;

namespace SoloCrm.Infrastructure.Persistence.Outbox;

/// <summary>
/// A domain event persisted in the same transaction as the change that raised it (ADR-010).
/// The outbox processor delivers it to the webhook subscriptions and retries with backoff (ADR-008).
/// </summary>
public sealed class OutboxMessage
{
    public const int TypeMaxLength = 100;
    public const int LastErrorMaxLength = 1000;

    // Required by EF Core.
    private OutboxMessage()
    {
        Type = null!;
        Payload = null!;
    }

    private OutboxMessage(string type, string payload, DateTimeOffset occurredAt)
    {
        Id = Guid.CreateVersion7();
        Type = type;
        Payload = payload;
        OccurredAt = occurredAt;
        NextAttemptAt = occurredAt;
    }

    /// <summary>Also the event id that webhook receivers use for deduplication.</summary>
    public Guid Id { get; private set; }

    /// <summary>Public event name, e.g. <c>opportunity.stage_changed</c>.</summary>
    public string Type { get; private set; }

    /// <summary>The serialized event (jsonb, camelCase).</summary>
    public string Payload { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>Number of delivery rounds so far.</summary>
    public int Attempts { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public string? LastError { get; private set; }

    public static OutboxMessage Create(IDomainEvent domainEvent, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var eventType = domainEvent.GetType();
        var payload = JsonSerializer.Serialize(domainEvent, eventType, JsonColumn.Options);
        return new OutboxMessage(DomainEventNames.Of(eventType), payload, occurredAt);
    }

    /// <summary>No subscription receives the event; nothing was sent (iteration 5 decision 3).</summary>
    public void MarkSkipped(DateTimeOffset processedAt) => ProcessedAt = processedAt;

    /// <summary>After a delivery round: every delivery succeeded or was given up.</summary>
    public void MarkProcessed(DateTimeOffset processedAt, string? lastError = null)
    {
        Attempts++;
        ProcessedAt = processedAt;
        LastError = Truncate(lastError);
    }

    /// <summary>After a delivery round: at least one delivery failed and will be retried at <paramref name="nextAttemptAt"/>.</summary>
    public void ScheduleRetry(DateTimeOffset nextAttemptAt, string lastError)
    {
        Attempts++;
        NextAttemptAt = nextAttemptAt;
        LastError = Truncate(lastError);
    }

    private static string? Truncate(string? error) =>
        error is { Length: > LastErrorMaxLength } ? error[..LastErrorMaxLength] : error;
}
