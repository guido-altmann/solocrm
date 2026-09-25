namespace SoloCrm.Domain.Common;

/// <summary>
/// Base class for all persisted entities (SPEC 2.2).
/// </summary>
public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected Entity()
    {
        Id = Guid.CreateVersion7();
    }

    public Guid Id { get; private set; }

    /// <summary>Set exclusively by the timestamp interceptor.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Set exclusively by the timestamp interceptor.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents() => _domainEvents.Clear();
}
