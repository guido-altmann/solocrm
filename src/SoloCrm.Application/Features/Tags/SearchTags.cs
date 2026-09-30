using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Tags;

/// <summary>
/// Autocomplete of the tag chip input and the tag filter (US-15, US-04 AK2): tags whose name contains the input,
/// those starting with it first. A single user has a few dozen tags, so they are filtered in memory.
/// </summary>
public static class SearchTags
{
    public const int DefaultLimit = 10;
    public const int MaxLimit = 50;

    public sealed record Query(string? Search, int Limit = DefaultLimit);

    public sealed record Result(IReadOnlyList<TagRef> Items);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.Limit)
                .InclusiveBetween(1, MaxLimit)
                .WithMessage($"Es können höchstens {MaxLimit} Treffer abgefragt werden.");
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
            var tags = await db.Tags.AsNoTracking()
                .OrderBy(t => t.Name)
                .Select(t => new TagRef(t.Id, t.Name, t.Color))
                .ToListAsync(cancellationToken);

            var search = query.Search?.Trim() ?? "";
            var items = tags
                .Where(t => t.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase))
                .OrderBy(t => t.Name.StartsWith(search, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1)
                .Take(query.Limit)
                .ToList();

            return new Result(items);
        }
    }
}
