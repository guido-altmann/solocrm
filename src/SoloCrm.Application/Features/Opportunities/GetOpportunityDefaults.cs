using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Opportunities;

namespace SoloCrm.Application.Features.Opportunities;

/// <summary>
/// Everything the request dialog needs besides the request itself: stages, pricing defaults and valuation settings.
/// </summary>
public static class GetOpportunityDefaults
{
    public sealed record Query;

    public sealed record StageItem(Guid Id, string Name, StageStatus Status);

    public sealed record Result(
        IReadOnlyList<StageItem> Stages,
        PricingModel DefaultPricingModel,
        string DefaultCurrency,
        ValuationSettings Valuation);

    public sealed class Handler(ICrmDbContextFactory dbFactory, IAppSettings settings) : IQueryHandler<Query, Result>
    {
        public async Task<Result<Result>> Handle(Query query, CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var stages = await db.Stages
                .AsNoTracking()
                .OrderBy(s => s.SortOrder)
                .Select(s => new StageItem(s.Id, s.Name, s.Status))
                .ToListAsync(cancellationToken);

            return new Result(
                stages,
                await settings.GetAsync(AppSettingKeys.DefaultPricingModel, cancellationToken),
                await settings.GetAsync(AppSettingKeys.DefaultCurrency, cancellationToken),
                await settings.GetValuationSettingsAsync(cancellationToken));
        }
    }
}
