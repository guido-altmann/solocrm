using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Opportunities;

namespace SoloCrm.Application.Features.Pipeline;

/// <summary>
/// The pipeline board: one column per stage with its active requests, the sum of <c>EstimatedValue</c> and,
/// if retainers are contained, the sum of MRR (US-07, SPEC 3.3 S2). Won/lost stages are drop zones whose cards
/// are only loaded on request ("Abgeschlossene", US-08 AK3). Sums are grouped by currency (no conversion).
/// Cards show the days since the last direct activity (or since creation), see <see cref="Card.DaysSinceActivity"/>.
/// </summary>
public static class GetPipelineBoard
{
    public sealed record Query(bool IncludeClosed = false);

    public sealed record MoneyTotal(string Currency, decimal Amount);

    public sealed record Card(
        Guid Id,
        Guid StageId,
        string Title,
        string? ClientName,
        string? AgencyName,
        string? PricingDisplay,
        string? Currency,
        decimal? EstimatedValue,
        decimal? MonthlyRecurringValue,
        LostReason? LostReason,
        DateTimeOffset? ClosedAt,
        DateTimeOffset LastActivityAt,
        int DaysSinceActivity);

    public sealed record Column(
        Guid StageId,
        string Name,
        StageStatus Status,
        IReadOnlyList<Card> Cards,
        IReadOnlyList<MoneyTotal> EstimatedValueTotals,
        IReadOnlyList<MoneyTotal> MonthlyRecurringTotals);

    public sealed record Result(IReadOnlyList<Column> Columns);

    public sealed class Handler(ICrmDbContextFactory dbFactory, IAppSettings settings, AppClock clock) : IQueryHandler<Query, Result>
    {
        public async Task<Result<Result>> Handle(Query query, CancellationToken cancellationToken)
        {
            var valuation = await settings.GetValuationSettingsAsync(cancellationToken);

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var stages = await db.Stages.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync(cancellationToken);

            var opportunities = await db.Opportunities
                .AsNoTracking()
                .Where(o => !o.IsArchived && (query.IncludeClosed || o.Stage!.Status == StageStatus.Open))
                .OrderByDescending(o => o.ClosedAt)
                .ThenByDescending(o => o.CreatedAt)
                .Select(o => new
                {
                    o.Id,
                    o.StageId,
                    o.Title,
                    ClientName = o.ClientOrganization!.Name,
                    AgencyName = o.AgencyOrganization!.Name,
                    o.Pricing,
                    o.Duration,
                    o.Utilization,
                    o.LostReason,
                    o.ClosedAt,
                    LastActivityAt = db.Activities.Where(a => a.OpportunityId == o.Id).Max(a => (DateTimeOffset?)a.OccurredAt) ?? o.CreatedAt,
                })
                .ToListAsync(cancellationToken);

            var cardsByStage = opportunities
                .Select(o => new Card(
                    o.Id,
                    o.StageId,
                    o.Title,
                    o.ClientName,
                    o.AgencyName,
                    o.Pricing?.ToDisplayString(),
                    o.Pricing?.Currency,
                    OpportunityValuation.EstimatedValue(o.Pricing, o.Duration, o.Utilization, valuation),
                    OpportunityValuation.MonthlyRecurringValue(o.Pricing),
                    o.LostReason,
                    o.ClosedAt,
                    o.LastActivityAt,
                    clock.DaysSince(o.LastActivityAt)))
                .ToLookup(c => c.StageId);

            var columns = stages
                .Select(s =>
                {
                    var cards = cardsByStage[s.Id].ToList();
                    return new Column(
                        s.Id,
                        s.Name,
                        s.Status,
                        cards,
                        Totals(cards, c => c.EstimatedValue),
                        Totals(cards, c => c.MonthlyRecurringValue));
                })
                .ToList();

            return new Result(columns);
        }

        private static List<MoneyTotal> Totals(IEnumerable<Card> cards, Func<Card, decimal?> value) =>
            cards
                .Where(c => c.Currency is not null && value(c) is not null)
                .GroupBy(c => c.Currency!, StringComparer.Ordinal)
                .Select(g => new MoneyTotal(g.Key, decimal.Round(g.Sum(c => value(c)!.Value), 2)))
                .OrderBy(t => t.Currency, StringComparer.Ordinal)
                .ToList();
    }
}
