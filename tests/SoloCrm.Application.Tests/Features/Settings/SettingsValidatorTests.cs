using SoloCrm.Application.Features.Settings;
using SoloCrm.Domain.Opportunities;

namespace SoloCrm.Application.Tests.Features.Settings;

public sealed class SettingsValidatorTests
{
    [Theory]
    [InlineData("EUR", 8, 12)]
    [InlineData(" chf ", 7.5, 1)]
    [InlineData("USD", 24, 120)]
    public void UpdatePricing_ValidValues_IsValid(string currency, decimal hoursPerDay, int months)
    {
        var validation = new UpdatePricingSettings.Validator().Validate(
            new UpdatePricingSettings.Command(PricingModel.Daily, currency, hoursPerDay, months));

        validation.IsValid.Should().BeTrue();
    }

    [Fact]
    public void UpdatePricing_InvalidValues_FailsPerField()
    {
        var validation = new UpdatePricingSettings.Validator().Validate(
            new UpdatePricingSettings.Command((PricingModel)42, "EURO", 0, 0));

        validation.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(
            "DefaultPricingModel", "DefaultCurrency", "HoursPerDay", "RetainerValuationMonths");
    }

    [Theory]
    [InlineData(24.5)]
    [InlineData(7.125)]
    public void UpdatePricing_HoursPerDayOutOfRangeOrTooPrecise_Fails(decimal hoursPerDay)
    {
        var validation = new UpdatePricingSettings.Validator().Validate(
            new UpdatePricingSettings.Command(PricingModel.Hourly, "EUR", hoursPerDay, 12));

        validation.Errors.Should().ContainSingle().Which.PropertyName.Should().Be("HoursPerDay");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(365, true)]
    [InlineData(366, false)]
    public void UpdateToday_Threshold_MustBeWithinRange(int days, bool valid)
    {
        new UpdateTodaySettings.Validator().Validate(new UpdateTodaySettings.Command(days)).IsValid.Should().Be(valid);
    }
}
