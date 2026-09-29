using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Opportunities;
using SoloCrm.Application.Features.Settings;
using SoloCrm.Domain.Opportunities;

namespace SoloCrm.IntegrationTests.Features.Settings;

public sealed class SettingsHandlerTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    [Fact]
    public async Task Get_NothingStored_ReturnsDefaults()
    {
        var result = await QueryAsync<GetSettings.Query, GetSettings.Result>(new GetSettings.Query());

        result.Value.Should().Be(new GetSettings.Result(PricingModel.Hourly, "EUR", 8m, 12, 7));
    }

    [Fact]
    public async Task UpdatePricingAndToday_ValidValues_ArePersistedAndUsedForValuation()
    {
        (await SendAsync<UpdatePricingSettings.Command, UpdatePricingSettings.Result>(
            new UpdatePricingSettings.Command(PricingModel.Daily, " chf ", 7.5m, 6))).IsSuccess.Should().BeTrue();
        (await SendAsync<UpdateTodaySettings.Command, UpdateTodaySettings.Result>(
            new UpdateTodaySettings.Command(14))).IsSuccess.Should().BeTrue();

        var settings = await QueryAsync<GetSettings.Query, GetSettings.Result>(new GetSettings.Query());

        settings.Value.Should().Be(new GetSettings.Result(PricingModel.Daily, "CHF", 7.5m, 6, 14));
        var id = (await SendAsync<CreateOpportunity.Command, CreateOpportunity.Result>(
            new CreateOpportunity.Command("Wartung", Amount: 1000m))).Value.Id;
        var opportunity = (await QueryAsync<GetOpportunity.Query, GetOpportunity.Result>(new GetOpportunity.Query(id))).Value;
        opportunity.Pricing.Should().Be(Pricing.Create(PricingModel.Daily, 1000m, "CHF"), "new requests use the defaults");
    }

    [Fact]
    public async Task Update_InvalidValues_ReturnValidationErrorAndKeepSettings()
    {
        (await SendAsync<UpdatePricingSettings.Command, UpdatePricingSettings.Result>(
            new UpdatePricingSettings.Command(PricingModel.Daily, "XXX", 8m, 12))).Error.Should().BeOfType<ValidationError>();
        (await SendAsync<UpdateTodaySettings.Command, UpdateTodaySettings.Result>(
            new UpdateTodaySettings.Command(0))).Error.Should().BeOfType<ValidationError>();

        (await QueryAsync<GetSettings.Query, GetSettings.Result>(new GetSettings.Query())).Value
            .Should().Be(new GetSettings.Result(PricingModel.Hourly, "EUR", 8m, 12, 7));
    }
}
