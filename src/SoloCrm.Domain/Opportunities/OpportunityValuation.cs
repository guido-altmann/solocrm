namespace SoloCrm.Domain.Opportunities;

/// <summary>
/// Calculates the derived values of an opportunity (SPEC 2.3, ADR-011). Never persisted.
/// </summary>
public static class OpportunityValuation
{
    private const int FullUtilization = 100;

    /// <summary>
    /// Estimated total value, or <c>null</c> if it cannot be calculated (no pricing; hourly/daily rate without duration).
    /// </summary>
    /// <remarks>
    /// Hourly: amount × hours/day × working days × utilization; daily: amount × working days × utilization;
    /// fixed price: amount; retainer: amount × months (without duration: × <see cref="ValuationSettings.RetainerValuationMonths"/>).
    /// Missing utilization counts as 100 %.
    /// </remarks>
    public static decimal? EstimatedValue(Pricing? pricing, Duration? duration, int? utilization, ValuationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (pricing is null)
        {
            return null;
        }

        var utilizationFactor = (utilization ?? FullUtilization) / (decimal)FullUtilization;

        return pricing.Model switch
        {
            PricingModel.Hourly => duration is null
                ? null
                : pricing.Amount * settings.HoursPerDay * duration.ToWorkingDays() * utilizationFactor,
            PricingModel.Daily => duration is null
                ? null
                : pricing.Amount * duration.ToWorkingDays() * utilizationFactor,
            PricingModel.FixedPrice => pricing.Amount,
            _ => pricing.Amount * (duration?.ToMonths() ?? settings.RetainerValuationMonths),
        };
    }

    /// <summary>Monthly recurring revenue: the monthly amount of a retainer, otherwise <c>null</c>.</summary>
    public static decimal? MonthlyRecurringValue(Pricing? pricing) =>
        pricing is { Model: PricingModel.Retainer } ? pricing.Amount : null;
}
