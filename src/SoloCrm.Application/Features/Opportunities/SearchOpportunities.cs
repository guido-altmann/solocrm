using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Search;
using SoloCrm.Domain.Opportunities;

namespace SoloCrm.Application.Features.Opportunities;

/// <summary>
/// Autocomplete over active requests by title (e.g. linking a task from „Heute“); open requests first, then by
/// relevance. Uses the search of the command palette (ADR-007).
/// </summary>
public static class SearchOpportunities
{
    public const int DefaultLimit = 10;
    public const int MaxLimit = 50;

    public sealed record Query(string? Search, int Limit = DefaultLimit);

    public sealed record Item(Guid Id, string Title, string? ClientName, StageStatus Status);

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

            var opportunities = db.Opportunities.AsNoTracking().Where(o => !o.IsArchived);
            var openFirst = opportunities.OrderBy(o => o.ClosedAt == null ? 0 : 1);

            if (SearchTerm.Parse(query.Search) is { } term)
            {
                openFirst = opportunities
                    .Where(SearchPredicates.OpportunityMatches(term))
                    .OrderBy(o => o.ClosedAt == null ? 0 : 1)
                    .ThenByDescending(SearchPredicates.OpportunityRelevance(term));
            }

            var items = await openFirst
                .ThenBy(o => o.Title)
                .ThenBy(o => o.Id)
                .Take(query.Limit)
                .Select(o => new Item(o.Id, o.Title, o.ClientOrganization!.Name, o.Stage!.Status))
                .ToListAsync(cancellationToken);

            return new Result(items);
        }
    }
}
