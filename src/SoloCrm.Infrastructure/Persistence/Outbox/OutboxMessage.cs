using System.Text;
using System.Text.Json;
using SoloCrm.Domain.Common;

namespace SoloCrm.Infrastructure.Persistence.Outbox;

/// <summary>
/// A domain event persisted in the same transaction as the change that raised it (ADR-010).
/// Processing and webhook delivery follow in iteration 5.
/// </summary>
public sealed class OutboxMessage
{
    public const int TypeMaxLength = 100;

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

    public int Attempts { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public string? LastError { get; private set; }

    public static OutboxMessage Create(IDomainEvent domainEvent, DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var eventType = domainEvent.GetType();
        var payload = JsonSerializer.Serialize(domainEvent, eventType, JsonColumn.Options);
        return new OutboxMessage(EventTypeName(eventType), payload, occurredAt);
    }

    /// <summary>
    /// Derives the public event name from the CLR type: the first word names the aggregate,
    /// the rest is snake_case (<c>OpportunityStageChanged</c> → <c>opportunity.stage_changed</c>).
    /// </summary>
    public static string EventTypeName(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        var name = eventType.Name;
        var builder = new StringBuilder(name.Length + 4);
        var separator = '.';
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c))
            {
                builder.Append(separator);
                separator = '_';
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }
}
