using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.Activities;

/// <summary>Raised when an activity is recorded. Subject and body are deliberately not part of the payload.</summary>
public sealed record ActivityLogged(
    Guid ActivityId,
    ActivityType ActivityType,
    DateTimeOffset OccurredAt,
    Guid? ContactId,
    Guid? OrganizationId,
    Guid? OpportunityId) : IDomainEvent;
