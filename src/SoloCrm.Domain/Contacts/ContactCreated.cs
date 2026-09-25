using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.Contacts;

public sealed record ContactCreated(Guid ContactId) : IDomainEvent;
