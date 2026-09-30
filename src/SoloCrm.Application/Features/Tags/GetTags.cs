using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Tags;

/// <summary>All tags with the number of assignments per record type, for the settings (US-15).</summary>
public static class GetTags
{
    public sealed record Query;

    public sealed record Item(Guid Id, string Name, string Color, int ContactCount, int OrganizationCount, int OpportunityCount)
    {
        public int AssignmentCount => ContactCount + OrganizationCount + OpportunityCount;
    }

    public sealed record Result(IReadOnlyList<Item> Items);

    public sealed class Validator : AbstractValidator<Query>;

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

            var items = await db.Tags.AsNoTracking()
                .OrderBy(t => t.Name)
                .Select(t => new Item(
                    t.Id,
                    t.Name,
                    t.Color,
                    db.ContactTags.Count(a => a.TagId == t.Id),
                    db.OrganizationTags.Count(a => a.TagId == t.Id),
                    db.OpportunityTags.Count(a => a.TagId == t.Id)))
                .ToListAsync(cancellationToken);

            return new Result(items);
        }
    }
}
