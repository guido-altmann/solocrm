using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.Contacts;

/// <summary>Raised by the GDPR erasure (US-20); carries only the id, so receivers can delete the person elsewhere.</summary>
public sealed record ContactDeleted(Guid ContactId) : IDomainEvent;
