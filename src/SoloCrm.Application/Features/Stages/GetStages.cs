using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Opportunities;

namespace SoloCrm.Application.Features.Stages;

/// <summary>
/// All stages in pipeline order with the number of assigned requests (incl. archived ones), for /settings (US-09).
/// </summary>
public static class GetStages
{
    public sealed record Query;

    public sealed record Item(Guid Id, string Name, StageStatus Status, int SortOrder, int OpportunityCount);

    public sealed record Result(IReadOnlyList<Item> Items);

    public sealed class Handler(ICrmDbContextFactory dbFactory) : IQueryHandler<Query, Result>
    {
        public async Task<Result<Result>> Handle(Query query, CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var items = await db.Stages
                .AsNoTracking()
                .OrderBy(s => s.SortOrder)
                .Select(s => new Item(s.Id, s.Name, s.Status, s.SortOrder, db.Opportunities.Count(o => o.StageId == s.Id)))
                .ToListAsync(cancellationToken);

            return new Result(items);
        }
    }
}
