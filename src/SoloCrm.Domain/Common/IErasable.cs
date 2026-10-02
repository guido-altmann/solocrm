namespace SoloCrm.Domain.Common;

/// <summary>
/// An entity whose hard deletion is the erasure of personal data (GDPR, US-20). The audit interceptor writes the
/// <c>Deleted</c> entry without field values and anonymizes the earlier entries of the entity and of the records
/// deleted with it through a cascading foreign key (ADR-006).
/// </summary>
public interface IErasable
{
    Guid Id { get; }
}
