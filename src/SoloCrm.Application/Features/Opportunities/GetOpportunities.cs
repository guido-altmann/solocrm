using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Search;
using SoloCrm.Application.Features.Tags;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Opportunities;

namespace SoloCrm.Application.Features.Opportunities;

/// <summary>
/// Pages through requests with the shared search (ADR-007), stage and tag filter (tags OR-combined), for the REST API
/// (SPEC 5). Search results are ordered by relevance, everything else newest first; archived ones only on request.
/// </summary>
public static class GetOpportunities
{
    public sealed record Query(
        string? Search = null,
        Guid? StageId = null,
        bool IncludeArchived = false,
        int PageIndex = 0,
        int PageSize = Paging.DefaultPageSize,
        IReadOnlyCollection<Guid>? TagIds = null) : IPagedQuery;

    public sealed record Item(
        Guid Id,
        string Title,
        Guid StageId,
        string StageName,
        StageStatus StageStatus,
        Guid? ClientOrganizationId,
        Guid? AgencyOrganizationId,
        Guid? PrimaryContactId,
        Pricing? Pricing,
        LeadSource? Source,
        bool IsArchived,
        DateOnly ReceivedOn,
        DateTimeOffset CreatedAt,
        IReadOnlyList<TagRef> Tags);

    public sealed record Result(IReadOnlyList<Item> Items, int TotalCount);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            Include(new PagingValidator<Query>());
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

            var opportunities = db.Opportunities.AsNoTracking();

            if (!query.IncludeArchived)
            {
                opportunities = opportunities.Where(o => !o.IsArchived);
            }

            if (query.StageId is { } stageId)
            {
                opportunities = opportunities.Where(o => o.StageId == stageId);
            }

            if (query.TagIds is { Count: > 0 } tagIds)
            {
                opportunities = opportunities.Where(o => db.OpportunityTags.Any(t => t.OpportunityId == o.Id && tagIds.Contains(t.TagId)));
            }

            var term = SearchTerm.Parse(query.Search);
            if (term is not null)
            {
                opportunities = opportunities.Where(SearchPredicates.OpportunityMatches(term));
            }

            var totalCount = await opportunities.CountAsync(cancellationToken);

            var sorted = term is not null
                ? opportunities.OrderByDescending(SearchPredicates.OpportunityRelevance(term))
                : opportunities.OrderByDescending(o => o.ReceivedOn).ThenByDescending(o => o.CreatedAt);

            var items = await sorted
                .ThenByDescending(o => o.Id)
                .Page(query)
                .Select(o => new Item(
                    o.Id,
                    o.Title,
                    o.StageId,
                    o.Stage!.Name,
                    o.Stage.Status,
                    o.ClientOrganizationId,
                    o.AgencyOrganizationId,
                    o.PrimaryContactId,
                    o.Pricing,
                    o.Source,
                    o.IsArchived,
                    o.ReceivedOn,
                    o.CreatedAt,
                    db.OpportunityTags
                        .Where(t => t.OpportunityId == o.Id)
                        .OrderBy(t => t.Tag!.Name)
                        .Select(t => new TagRef(t.TagId, t.Tag!.Name, t.Tag.Color))
                        .ToList()))
                .ToListAsync(cancellationToken);

            return new Result(items, totalCount);
        }
    }
}
