using SoloCrm.Domain.Opportunities;

namespace SoloCrm.Domain.Tests.Opportunities;

/// <summary>
/// EstimatedValue for all four pricing models × duration units × missing values (SPEC 2.3), default settings 8 h/day, 12 months.
/// </summary>
public sealed class OpportunityValuationTests
{
    private static readonly ValuationSettings Settings = ValuationSettings.Default;

    public static TheoryData<PricingModel, decimal, int?, DurationUnit?, int?, decimal?> Cases => new()
    {
        // Hourly: amount × 8 h × working days × utilization
        { PricingModel.Hourly, 100m, 10, DurationUnit.Days, null, 8_000m },
        { PricingModel.Hourly, 100m, 2, DurationUnit.Weeks, null, 8_000m },
        { PricingModel.Hourly, 100m, 3, DurationUnit.Months, null, 48_000m },
        { PricingModel.Hourly, 100m, 1, DurationUnit.Months, 50, 8_000m },
        { PricingModel.Hourly, 95m, 4, DurationUnit.Weeks, 80, 12_160m },
        { PricingModel.Hourly, 100m, 1, DurationUnit.Days, 0, 0m },
        { PricingModel.Hourly, 100m, null, null, null, null },
        { PricingModel.Hourly, 100m, null, null, 80, null },

        // Daily: amount × working days × utilization
        { PricingModel.Daily, 800m, 10, DurationUnit.Days, null, 8_000m },
        { PricingModel.Daily, 800m, 4, DurationUnit.Weeks, null, 16_000m },
        { PricingModel.Daily, 800m, 2, DurationUnit.Months, null, 32_000m },
        { PricingModel.Daily, 800m, 1, DurationUnit.Weeks, 60, 2_400m },
        { PricingModel.Daily, 800m, null, null, null, null },

        // Fixed price: amount, independent of duration and utilization
        { PricingModel.FixedPrice, 8_000m, null, null, null, 8_000m },
        { PricingModel.FixedPrice, 8_000m, 3, DurationUnit.Months, 50, 8_000m },
        { PricingModel.FixedPrice, 8_000m, 10, DurationUnit.Days, null, 8_000m },

        // Retainer: amount × months; days/weeks → working days / 20 rounded up; no duration → 12 months
        { PricingModel.Retainer, 2_500m, null, null, null, 30_000m },
        { PricingModel.Retainer, 2_500m, 3, DurationUnit.Months, null, 7_500m },
        { PricingModel.Retainer, 2_500m, 20, DurationUnit.Days, null, 2_500m },
        { PricingModel.Retainer, 2_500m, 21, DurationUnit.Days, null, 5_000m },
        { PricingModel.Retainer, 2_500m, 4, DurationUnit.Weeks, null, 2_500m },
        { PricingModel.Retainer, 2_500m, 5, DurationUnit.Weeks, null, 5_000m },
        { PricingModel.Retainer, 2_500m, 6, DurationUnit.Months, 50, 15_000m },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void EstimatedValue_ModelDurationAndUtilization_MatchesFormula(
        PricingModel model,
        decimal amount,
        int? durationValue,
        DurationUnit? durationUnit,
        int? utilization,
        decimal? expected)
    {
        var duration = durationValue is { } value ? Duration.Create(value, durationUnit!.Value) : null;

        var result = OpportunityValuation.EstimatedValue(Pricing.Create(model, amount), duration, utilization, Settings);

        result.Should().Be(expected);
    }

    [Fact]
    public void EstimatedValue_NoPricing_ReturnsNull()
    {
        OpportunityValuation.EstimatedValue(null, Duration.Create(3, DurationUnit.Months), 100, Settings).Should().BeNull();
    }

    [Fact]
    public void EstimatedValue_CustomHoursPerDay_UsesSetting()
    {
        var result = OpportunityValuation.EstimatedValue(
            Pricing.Create(PricingModel.Hourly, 100m), Duration.Create(10, DurationUnit.Days), null, new ValuationSettings(7.5m, 12));

        result.Should().Be(7_500m);
    }

    [Fact]
    public void EstimatedValue_RetainerWithoutDuration_UsesValuationMonthsSetting()
    {
        var result = OpportunityValuation.EstimatedValue(Pricing.Create(PricingModel.Retainer, 2_500m), null, null, new ValuationSettings(8m, 6));

        result.Should().Be(15_000m);
    }

    [Theory]
    [InlineData(PricingModel.Retainer, 2_500)]
    [InlineData(PricingModel.Hourly, null)]
    [InlineData(PricingModel.Daily, null)]
    [InlineData(PricingModel.FixedPrice, null)]
    public void MonthlyRecurringValue_Model_OnlyRetainerHasMrr(PricingModel model, int? expected)
    {
        OpportunityValuation.MonthlyRecurringValue(Pricing.Create(model, 2_500m)).Should().Be(expected);
    }

    [Fact]
    public void MonthlyRecurringValue_NoPricing_ReturnsNull()
    {
        OpportunityValuation.MonthlyRecurringValue(null).Should().BeNull();
    }
}
