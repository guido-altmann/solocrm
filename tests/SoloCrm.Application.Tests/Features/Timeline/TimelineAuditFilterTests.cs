using SoloCrm.Application.Features.Timeline;
using SoloCrm.Domain.Auditing;

namespace SoloCrm.Application.Tests.Features.Timeline;

public sealed class TimelineAuditFilterTests
{
    [Theory]
    [InlineData("StageId", true)]
    [InlineData("Pricing.Amount", true)]
    [InlineData("Duration.Unit", true)]
    [InlineData("OrganizationId", true)]
    [InlineData("IsArchived", true)]
    [InlineData("Phone", false)]
    [InlineData("Title", false)]
    [InlineData("Utilization", false)]
    public void IsVisible_Field_MatchesSpec(string field, bool visible)
    {
        TimelineAuditFilter.IsVisible(new AuditChange(field, "a", "b")).Should().Be(visible);
    }

    [Fact]
    public void FormatPricing_CompleteState_UsesModelFormat()
    {
        var state = new Dictionary<string, string?>
        {
            ["Pricing.Model"] = "Retainer",
            ["Pricing.Amount"] = "2500",
            ["Pricing.Currency"] = "EUR",
        };

        TimelineAuditFilter.FormatPricing(state).Should().Be("2.500 €/Monat");
    }

    [Fact]
    public void FormatPricing_NoAmountOrIncompleteState_FallsBack()
    {
        TimelineAuditFilter.FormatPricing(new Dictionary<string, string?>()).Should().Be("–");
        TimelineAuditFilter.FormatPricing(new Dictionary<string, string?> { ["Pricing.Amount"] = "95" }).Should().Be("95");
    }

    [Fact]
    public void FormatDuration_StateOrMissing_IsReadable()
    {
        TimelineAuditFilter.FormatDuration(new Dictionary<string, string?> { ["Duration.Value"] = "6", ["Duration.Unit"] = "Months" })
            .Should().Be("6 Monate");
        TimelineAuditFilter.FormatDuration(new Dictionary<string, string?>()).Should().Be("offen");
    }
}
