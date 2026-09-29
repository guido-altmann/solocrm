using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Opportunities;

namespace SoloCrm.Application.Features.Opportunities;

/// <summary>
/// Requests linked to a contact (as primary contact) or an organization (as client or agency), for the
/// detail views (SPEC 3.3 S5). Archived requests are included and flagged; open ones come first.
/// </summary>
public static class GetRelatedOpportunities
{
    public sealed record Query(Guid? PrimaryContactId = null, Guid? OrganizationId = null);

    public enum Role
    {
        PrimaryContact,
        Client,
        Agency,
    }

    public sealed record Item(
        Guid Id,
        string Title,
        Role Role,
        string StageName,
        StageStatus Status,
        string? PricingDisplay,
        bool IsArchived);

    public sealed record Result(IReadOnlyList<Item> Items);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q)
                .Must(q => q.PrimaryContactId is null != q.OrganizationId is null)
                .WithName("Query")
                .WithMessage("Bitte genau einen Kontakt oder eine Organisation angeben.");
        }
    }

    public sealed class Handler(ICrmDbContextFactory dbFactory, IValidator<Query> validator) : IQueryHandler<Query, Result>
    {
        public async Task<Result<Result>> Handle(Query query, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(query, cancellationToken);
            if (!validation.IsValid)
            {
                return validation.ToValidationError();
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var opportunities = query.PrimaryContactId is { } contactId
                ? db.Opportunities.Where(o => o.PrimaryContactId == contactId)
                : db.Opportunities.Where(o => o.ClientOrganizationId == query.OrganizationId || o.AgencyOrganizationId == query.OrganizationId);

            var rows = await opportunities
                .AsNoTracking()
                .OrderBy(o => o.IsArchived)
                .ThenBy(o => o.ClosedAt != null)
                .ThenByDescending(o => o.CreatedAt)
                .Select(o => new
                {
                    o.Id,
                    o.Title,
                    IsClient = o.ClientOrganizationId == query.OrganizationId,
                    StageName = o.Stage!.Name,
                    StageStatus = o.Stage.Status,
                    o.Pricing,
                    o.IsArchived,
                })
                .ToListAsync(cancellationToken);

            var items = rows
                .Select(o => new Item(
                    o.Id,
                    o.Title,
                    query.PrimaryContactId is not null ? Role.PrimaryContact : o.IsClient ? Role.Client : Role.Agency,
                    o.StageName,
                    o.StageStatus,
                    o.Pricing?.ToDisplayString(),
                    o.IsArchived))
                .ToList();

            return new Result(items);
        }
    }
}
