namespace SoloCrm.Domain.Common;

/// <summary>
/// The records an activity or task refers to (SPEC 2.3). Activities need at least one, tasks may have none.
/// </summary>
public sealed record LinkedRecords(Guid? ContactId = null, Guid? OrganizationId = null, Guid? OpportunityId = null)
{
    public static LinkedRecords None { get; } = new();

    public bool HasAny => ContactId is not null || OrganizationId is not null || OpportunityId is not null;
}
