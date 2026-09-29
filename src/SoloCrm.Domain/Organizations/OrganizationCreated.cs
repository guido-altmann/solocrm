using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.Organizations;

public sealed record OrganizationCreated(Guid OrganizationId) : IDomainEvent;
