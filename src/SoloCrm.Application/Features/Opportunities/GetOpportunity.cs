using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Application.Features.Opportunities;

/// <summary>
/// Loads one request incl. names of related objects and the calculated values (edit dialog).
/// </summary>
public static class GetOpportunity
{
    public sealed record Query(Guid Id);

    public sealed record Result(
        Guid Id,
        string Title,
        DateOnly ReceivedOn,
        Guid StageId,
        string StageName,
        StageStatus StageStatus,
        LostReason? LostReason,
        DateTimeOffset? ClosedAt,
        Guid? ClientOrganizationId,
        string? ClientOrganizationName,
        OrganizationType? ClientOrganizationType,
        Guid? AgencyOrganizationId,
        string? AgencyOrganizationName,
        OrganizationType? AgencyOrganizationType,
        Guid? PrimaryContactId,
        string? PrimaryContactName,
        Pricing? Pricing,
        DateOnly? StartDate,
        Duration? Duration,
        int? Utilization,
        int? RemotePercentage,
        LeadSource? Source,
        bool IsArchived,
        decimal? EstimatedValue,
        decimal? MonthlyRecurringValue);

    public sealed class Handler(ICrmDbContextFactory dbFactory, IAppSettings settings) : IQueryHandler<Query, Result>
    {
        public async Task<Result<Result>> Handle(Query query, CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var opportunity = await db.Opportunities
                .AsNoTracking()
                .Include(o => o.Stage)
                .Include(o => o.ClientOrganization)
                .Include(o => o.AgencyOrganization)
                .Include(o => o.PrimaryContact)
                .SingleOrDefaultAsync(o => o.Id == query.Id, cancellationToken);

            if (opportunity is null)
            {
                return OpportunityErrors.NotFound;
            }

            var valuation = await settings.GetValuationSettingsAsync(cancellationToken);
            var contact = opportunity.PrimaryContact;

            return new Result(
                opportunity.Id,
                opportunity.Title,
                opportunity.ReceivedOn,
                opportunity.StageId,
                opportunity.Stage!.Name,
                opportunity.Stage.Status,
                opportunity.LostReason,
                opportunity.ClosedAt,
                opportunity.ClientOrganizationId,
                opportunity.ClientOrganization?.Name,
                opportunity.ClientOrganization?.Type,
                opportunity.AgencyOrganizationId,
                opportunity.AgencyOrganization?.Name,
                opportunity.AgencyOrganization?.Type,
                opportunity.PrimaryContactId,
                contact is null ? null : string.Join(" ", new[] { contact.FirstName, contact.LastName }.Where(n => n is not null)),
                opportunity.Pricing,
                opportunity.StartDate,
                opportunity.Duration,
                opportunity.Utilization,
                opportunity.RemotePercentage,
                opportunity.Source,
                opportunity.IsArchived,
                opportunity.EstimatedValue(valuation),
                opportunity.MonthlyRecurringValue());
        }
    }
}
