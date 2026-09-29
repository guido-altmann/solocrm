using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Application.Features.Organizations;

/// <summary>
/// Autocomplete over active organizations. Organizations of the preferred type are listed first,
/// all others stay selectable (US-02 AK1, US-06 AK2).
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
            var startsWith = "";

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var contains = LikePattern.Contains(query.Search);
                startsWith = LikePattern.StartsWith(query.Search);
                organizations = organizations.Where(o => EF.Functions.ILike(o.Name, contains, LikePattern.Escape));
            }

            var items = await organizations
                .OrderBy(o => o.Type == query.PreferredType ? 0 : 1)
                .ThenBy(o => EF.Functions.ILike(o.Name, startsWith, LikePattern.Escape) ? 0 : 1)
                .ThenBy(o => o.Name)
                .ThenBy(o => o.Id)
                .Take(query.Limit)
                .Select(o => new Item(o.Id, o.Name, o.Type))
                .ToListAsync(cancellationToken);

            return new Result(items);
        }
    }
}
