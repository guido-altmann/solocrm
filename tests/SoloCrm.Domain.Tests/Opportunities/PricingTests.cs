using SoloCrm.Domain.Opportunities;

namespace SoloCrm.Domain.Tests.Opportunities;

public sealed class PricingTests
{
    [Fact]
    public void Create_ValidValues_DefaultsToEuro()
    {
        var pricing = Pricing.Create(PricingModel.Hourly, 95m);

        pricing.Model.Should().Be(PricingModel.Hourly);
        pricing.Amount.Should().Be(95m);
        pricing.Currency.Should().Be("EUR");
    }

    [Fact]
    public void Create_SameValues_AreEqual()
    {
        Pricing.Create(PricingModel.Daily, 760m, "CHF").Should().Be(Pricing.Create(PricingModel.Daily, 760m, "CHF"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(95.555)]
    [InlineData(10_000_000_000)]
    public void Create_InvalidAmount_Throws(decimal amount)
    {
        var act = () => Pricing.Create(PricingModel.Hourly, amount);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("eur")]
    [InlineData("EURO")]
    [InlineData("XYZ")]
    public void Create_InvalidCurrency_Throws(string currency)
    {
        var act = () => Pricing.Create(PricingModel.Hourly, 95m, currency);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_UnknownModel_Throws()
    {
        var act = () => Pricing.Create((PricingModel)99, 95m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(PricingModel.Hourly, 95, "EUR", "95 €/h")]
    [InlineData(PricingModel.Hourly, 95.5, "EUR", "95,50 €/h")]
    [InlineData(PricingModel.Daily, 760, "EUR", "760 €/Tag")]
    [InlineData(PricingModel.FixedPrice, 8000, "EUR", "8.000 € fix")]
    [InlineData(PricingModel.Retainer, 2500, "EUR", "2.500 €/Monat")]
    [InlineData(PricingModel.Retainer, 1250000, "USD", "1.250.000 $/Monat")]
    [InlineData(PricingModel.Daily, 1100, "CHF", "1.100 CHF/Tag")]
    public void ToDisplayString_Model_UsesModelFormat(PricingModel model, decimal amount, string currency, string expected)
    {
        Pricing.Create(model, amount, currency).ToDisplayString().Should().Be(expected);
    }
}
