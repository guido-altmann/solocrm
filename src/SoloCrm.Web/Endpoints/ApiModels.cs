using SoloCrm.Application.Features.Tags;
using SoloCrm.Domain.Activities;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Web.Endpoints;

// The REST API contract (SPEC 5). Addresses are flat fields (street … countryCode, ISO 3166-1 alpha-2) like the price. Explicit records instead of the handler results, so that the OpenAPI schemas have
// unique names and the contract does not change with the UI's needs.

public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed record TagResponse(Guid Id, string Name)
{
    public static IReadOnlyList<TagResponse> From(IEnumerable<TagRef> tags) => [.. tags.Select(t => new TagResponse(t.Id, t.Name))];
}

public sealed record ContactListItem(
    Guid Id,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? JobTitle,
    Guid? OrganizationId,
    string? OrganizationName,
    LeadSource? Source,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    IReadOnlyList<TagResponse> Tags);

public sealed record ContactDetail(
    Guid Id,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    string? JobTitle,
    string? LinkedInUrl,
    Guid? OrganizationId,
    string? OrganizationName,
    LeadSource? Source,
    bool IsArchived,
    string? Street,
    string? Street2,
    string? PostalCode,
    string? City,
    string? Region,
    string? CountryCode,
    IReadOnlyList<TagResponse> Tags);

/// <param name="OrganizationName">Uses the organization with this name (case-insensitive) or creates it; ignored when
/// <paramref name="OrganizationId"/> is set.</param>
public sealed record CreateContactRequest(
    string? FirstName,
    string? LastName,
    string? Email = null,
    string? Phone = null,
    string? JobTitle = null,
    string? LinkedInUrl = null,
    Guid? OrganizationId = null,
    string? OrganizationName = null,
    LeadSource? Source = null,
    string? Street = null,
    string? Street2 = null,
    string? PostalCode = null,
    string? City = null,
    string? Region = null,
    string? CountryCode = null);

public sealed record OrganizationListItem(
    Guid Id,
    string Name,
    OrganizationType Type,
    string? Website,
    string? City,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    IReadOnlyList<TagResponse> Tags);

public sealed record OrganizationDetail(
    Guid Id,
    string Name,
    OrganizationType Type,
    string? Website,
    string? Street,
    string? Street2,
    string? PostalCode,
    string? City,
    string? Region,
    string? CountryCode,
    string? Notes,
    bool IsArchived,
    IReadOnlyList<TagResponse> Tags);

public sealed record CreateOrganizationRequest(
    string? Name,
    OrganizationType? Type = null,
    string? Website = null,
    string? Street = null,
    string? Street2 = null,
    string? PostalCode = null,
    string? City = null,
    string? Region = null,
    string? CountryCode = null,
    string? Notes = null);

public sealed record OpportunityListItem(
    Guid Id,
    string Title,
    Guid StageId,
    string StageName,
    StageStatus StageStatus,
    Guid? ClientOrganizationId,
    Guid? AgencyOrganizationId,
    Guid? PrimaryContactId,
    PricingModel? PricingModel,
    decimal? Amount,
    string? Currency,
    LeadSource? Source,
    bool IsArchived,
    DateOnly ReceivedOn,
    DateTimeOffset CreatedAt,
    IReadOnlyList<TagResponse> Tags);

/// <param name="ReceivedOn">When the request came in; may lie before the recording.</param>
public sealed record OpportunityDetail(
    Guid Id,
    string Title,
    Guid StageId,
    string StageName,
    StageStatus StageStatus,
    LostReason? LostReason,
    DateTimeOffset? ClosedAt,
    Guid? ClientOrganizationId,
    Guid? AgencyOrganizationId,
    Guid? PrimaryContactId,
    PricingModel? PricingModel,
    decimal? Amount,
    string? Currency,
    DateOnly? StartDate,
    int? DurationValue,
    DurationUnit? DurationUnit,
    int? Utilization,
    int? RemotePercentage,
    LeadSource? Source,
    DateOnly ReceivedOn,
    bool IsArchived,
    decimal? EstimatedValue,
    decimal? MonthlyRecurringValue,
    IReadOnlyList<TagResponse> Tags);

public sealed record CreateOpportunityRequest(
    string? Title,
    Guid? StageId = null,
    Guid? ClientOrganizationId = null,
    Guid? AgencyOrganizationId = null,
    Guid? PrimaryContactId = null,
    PricingModel? PricingModel = null,
    decimal? Amount = null,
    string? Currency = null,
    DateOnly? StartDate = null,
    int? DurationValue = null,
    DurationUnit? DurationUnit = null,
    int? Utilization = null,
    int? RemotePercentage = null,
    LeadSource? Source = null,
    DateOnly? ReceivedOn = null);

/// <param name="Type">Default: <c>Note</c>.</param>
/// <param name="OccurredAt">Default: now; not in the future.</param>
public sealed record CreateActivityRequest(
    ActivityType? Type,
    string? Body,
    string? Subject = null,
    DateTimeOffset? OccurredAt = null,
    Guid? ContactId = null,
    Guid? OrganizationId = null,
    Guid? OpportunityId = null);

public sealed record CreatedResponse(Guid Id);

public sealed record TaskDetail(
    Guid Id,
    string Title,
    DateOnly? DueDate,
    bool Completed,
    DateTimeOffset? CompletedAt,
    Guid? ContactId,
    Guid? OrganizationId,
    Guid? OpportunityId);

public sealed record CreateTaskRequest(
    string? Title,
    DateOnly? DueDate = null,
    Guid? ContactId = null,
    Guid? OrganizationId = null,
    Guid? OpportunityId = null);

public sealed record StageResponse(Guid Id, string Name, StageStatus Status, int SortOrder);
