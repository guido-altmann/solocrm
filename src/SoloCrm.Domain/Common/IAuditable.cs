namespace SoloCrm.Domain.Common;

/// <summary>
/// Marks entities whose changes the audit interceptor records as <c>AuditEntry</c> (ADR-006).
/// </summary>
public interface IAuditable
{
    Guid Id { get; }
}
