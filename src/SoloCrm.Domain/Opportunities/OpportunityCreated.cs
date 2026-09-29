using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.Opportunities;

public sealed record OpportunityCreated(Guid OpportunityId, Guid StageId) : IDomainEvent;
