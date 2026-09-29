using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.Opportunities;

/// <summary>Raised on every stage change; the payload matches the webhook example in SPEC 5.</summary>
public sealed record OpportunityStageChanged(Guid OpportunityId, Guid FromStageId, Guid ToStageId, StageStatus ToStageStatus)
    : IDomainEvent;
