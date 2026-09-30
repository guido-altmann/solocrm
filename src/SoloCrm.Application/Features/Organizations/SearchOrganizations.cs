using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Search;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Application.Features.Organizations;

/// <summary>
/// Autocomplete over active organizations. Organizations of the preferred type are listed first,
/// all others stay selectable (US-02 AK1, US-06 AK2). Uses the search of the command palette (ADR-007); within a
/// type, hits are ordered by relevance.
/// </summary>
public static class SearchOrganizations
{
    public const int DefaultLimit = 10;
    public const int MaxLimit = 50;

    public sealed record Query(string? Search, OrganizationType? PreferredType = null, int Limit = DefaultLimit);

    public sealed record Item(Guid Id, string Name, OrganizationType Type);

    public sealed record Result(IReadOnlyList<Item> Items);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.Limit)
                .InclusiveBetween(1, MaxLimit)
                .WithMessage($"Es können höchstens {MaxLimit} Treffer abgefragt werden.");
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

            var organizations = db.Organizations.AsNoTracking().Where(o => !o.IsArchived);
            var byType = organizations.OrderBy(o => o.Type == query.PreferredType ? 0 : 1);

            if (SearchTerm.Parse(query.Search) is { } term)
            {
                byType = organizations
                    .Where(SearchPredicates.OrganizationMatches(term))
                    .OrderBy(o => o.Type == query.PreferredType ? 0 : 1)
                    .ThenByDescending(SearchPredicates.OrganizationRelevance(term));
            }

            var items = await byType
                .ThenBy(o => o.Name)
                .ThenBy(o => o.Id)
                .Take(query.Limit)
                .Select(o => new Item(o.Id, o.Name, o.Type))
                .ToListAsync(cancellationToken);

            return new Result(items);
        }
    }
}
