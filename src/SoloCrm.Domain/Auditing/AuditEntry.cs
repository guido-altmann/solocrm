namespace SoloCrm.Domain.Auditing;

/// <summary>
/// One recorded change of an auditable entity (SPEC 2.4). Written exclusively by the audit interceptor.
/// </summary>
public sealed class AuditEntry
{
    public const int EntityTypeMaxLength = 100;

    // Required by EF Core.
    private AuditEntry()
    {
        EntityType = null!;
        Changes = null!;
    }

    public AuditEntry(string entityType, Guid entityId, AuditAction action, IReadOnlyList<AuditChange> changes, DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentNullException.ThrowIfNull(changes);

        Id = Guid.CreateVersion7();
        EntityType = entityType;
        EntityId = entityId;
        Action = action;
        Changes = changes;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }

    /// <summary>CLR type name of the entity, e.g. <c>Contact</c>.</summary>
    public string EntityType { get; private set; }

    public Guid EntityId { get; private set; }

    public AuditAction Action { get; private set; }

    public IReadOnlyList<AuditChange> Changes { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }
}
