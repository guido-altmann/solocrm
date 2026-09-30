using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Search;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Application.Features.Organizations;

/// <summary>
/// Pages through organizations with search (name, website, city; same search as the command palette, ADR-007), type
/// filter and sorting; archived ones only on request (US-03, US-04, US-05). Without an explicit sort field, search
/// results are ordered by relevance and everything else by name.
/// </summary>
public static class GetOrganizations
{
    public enum SortField
    {
        Name,
        Type,
        City,
        CreatedAt,
    }

    public sealed record Query(
        string? Search = null,
        OrganizationType? Type = null,
        bool IncludeArchived = false,
        SortField? SortBy = null,
        bool SortDescending = false,
        int PageIndex = 0,
        int PageSize = Paging.DefaultPageSize) : IPagedQuery;

    public sealed record Item(
        Guid Id,
        string Name,
        OrganizationType Type,
        string? Website,
        string? City,
        bool IsArchived,
        DateTimeOffset CreatedAt);

    public sealed record Result(IReadOnlyList<Item> Items, int TotalCount);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            Include(new PagingValidator<Query>());
            RuleFor(q => q.Type).IsInEnum().WithMessage("Unbekannter Typ.");
            RuleFor(q => q.SortBy).IsInEnum().WithMessage("Unbekannte Sortierung.");
        }
    }

    public sealed class Handler(ICrmDbContextFactory dbFactory, IValidator<Query> validator)
        : IQueryHandler<Query, Result>
    {
        public async Task<Result<Result>> Handle(Query query, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(query, cancellationToken);
            if (!validation.IsValid)
            {
                return validation.ToValidationError();
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var organizations = db.Organizations.AsNoTracking();

            if (!query.IncludeArchived)
            {
                organizations = organizations.Where(o => !o.IsArchived);
            }

            if (query.Type is { } type)
            {
                organizations = organizations.Where(o => o.Type == type);
            }

            var term = SearchTerm.Parse(query.Search);
            if (term is not null)
            {
                organizations = organizations.Where(SearchPredicates.OrganizationMatches(term));
            }

            var totalCount = await organizations.CountAsync(cancellationToken);

            var sorted = query.SortBy is null && term is not null
                ? organizations.OrderByDescending(SearchPredicates.OrganizationRelevance(term))
                : Sort(organizations, query.SortBy ?? SortField.Name, query.SortDescending);

            var items = await sorted
                .ThenBy(o => o.Id)
                .Page(query)
                .Select(o => new Item(o.Id, o.Name, o.Type, o.Website, o.City, o.IsArchived, o.CreatedAt))
                .ToListAsync(cancellationToken);

            return new Result(items, totalCount);
        }

        private static IOrderedQueryable<Organization> Sort(IQueryable<Organization> source, SortField field, bool descending) =>
            field switch
            {
                SortField.Type => descending ? source.OrderByDescending(o => o.Type) : source.OrderBy(o => o.Type),
                SortField.City => descending ? source.OrderByDescending(o => o.City) : source.OrderBy(o => o.City),
                SortField.CreatedAt => descending ? source.OrderByDescending(o => o.CreatedAt) : source.OrderBy(o => o.CreatedAt),
                _ => descending ? source.OrderByDescending(o => o.Name) : source.OrderBy(o => o.Name),
            };
    }
}
