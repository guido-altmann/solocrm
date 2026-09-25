namespace SoloCrm.Domain.Common;

/// <summary>
/// Marker for events raised by entities. They are persisted to the outbox
/// within the same transaction as the change that raised them (ADR-010).
/// </summary>
public interface IDomainEvent;
