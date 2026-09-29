using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Opportunities;

namespace SoloCrm.Application.Features.Settings;

/// <summary>User-facing settings with their defaults (SPEC 2.4, S6): pricing defaults and the „Heute“ threshold.</summary>
public static class GetSettings
{
    public sealed record Query;

    public sealed record Result(
        PricingModel DefaultPricingModel,
        string DefaultCurrency,
        decimal HoursPerDay,
        int RetainerValuationMonths,
        int StaleOpportunityDays);

    public sealed class Handler(IAppSettings settings) : IQueryHandler<Query, Result>
    {
        public async Task<Result<Result>> Handle(Query query, CancellationToken cancellationToken) =>
            new Result(
                await settings.GetAsync(AppSettingKeys.DefaultPricingModel, cancellationToken),
                await settings.GetAsync(AppSettingKeys.DefaultCurrency, cancellationToken),
                await settings.GetAsync(AppSettingKeys.HoursPerDay, cancellationToken),
                await settings.GetAsync(AppSettingKeys.RetainerValuationMonths, cancellationToken),
                await settings.GetAsync(AppSettingKeys.StaleOpportunityDays, cancellationToken));
    }
}
