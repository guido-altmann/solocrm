using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.Opportunities;

/// <summary>
/// The editable fields of an <see cref="Opportunity"/> besides title and stage.
/// </summary>
public sealed record OpportunityDetails(
    Guid? ClientOrganizationId = null,
    Guid? AgencyOrganizationId = null,
    Guid? PrimaryContactId = null,
    Pricing? Pricing = null,
    DateOnly? StartDate = null,
    Duration? Duration = null,
    int? Utilization = null,
    int? RemotePercentage = null,
    LeadSource? Source = null);
