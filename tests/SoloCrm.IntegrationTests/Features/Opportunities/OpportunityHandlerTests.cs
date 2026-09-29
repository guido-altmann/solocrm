using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Application.Features.Opportunities;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Domain.Organizations;
using SoloCrm.Infrastructure.Persistence.Seeding;

namespace SoloCrm.IntegrationTests.Features.Opportunities;

public sealed class OpportunityHandlerTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    [Fact]
    public async Task Create_TitleOnly_StartsInFirstOpenStageAndWritesEvent()
    {
        var result = await CreateAsync(new CreateOpportunity.Command(" Migration Azure "));

        await using var db = OpenDb();
        var stored = await db.Opportunities.SingleAsync(o => o.Id == result, Ct);
        stored.Title.Should().Be("Migration Azure");
        stored.StageId.Should().Be(DefaultStages.New);
        stored.Pricing.Should().BeNull();
        (await db.OutboxMessages.SingleAsync(Ct)).Type.Should().Be("opportunity.created");
    }

    [Fact]
    public async Task Create_AmountWithoutModelAndCurrency_UsesSettingsDefaults()
    {
        await Get<IAppSettings>().SetAsync(AppSettingKeys.DefaultPricingModel, PricingModel.Daily, Ct);
        await Get<IAppSettings>().SetAsync(AppSettingKeys.DefaultCurrency, "CHF", Ct);

        var id = await CreateAsync(new CreateOpportunity.Command("Migration", Amount: 1100m));

        await using var db = OpenDb();
        (await db.Opportunities.SingleAsync(o => o.Id == id, Ct)).Pricing.Should().Be(Pricing.Create(PricingModel.Daily, 1100m, "CHF"));
    }

    [Fact]
    public async Task Create_AllFields_PersistsAndGetReturnsNamesAndValues()
    {
        var client = await CreateOrganizationAsync("Contoso Bank", OrganizationType.Client);
        var agency = await CreateOrganizationAsync("Hays", OrganizationType.Agency);
        var contact = (await SendAsync<CreateContact.Command, CreateContact.Result>(new CreateContact.Command("Ada", "Lovelace"))).Value.Id;

        var id = await CreateAsync(new CreateOpportunity.Command(
            "Migration Azure", DefaultStages.Applied, client, agency, contact, PricingModel.Hourly, 95m, "EUR",
            new DateOnly(2026, 11, 1), 6, DurationUnit.Months, 80, 100, Domain.Common.LeadSource.ProjectPortal));

        var loaded = (await QueryAsync<GetOpportunity.Query, GetOpportunity.Result>(new GetOpportunity.Query(id))).Value;
        loaded.StageName.Should().Be("Beworben");
        loaded.ClientOrganizationName.Should().Be("Contoso Bank");
        loaded.AgencyOrganizationName.Should().Be("Hays");
        loaded.AgencyOrganizationType.Should().Be(OrganizationType.Agency);
        loaded.PrimaryContactName.Should().Be("Ada Lovelace");
        loaded.Pricing.Should().Be(Pricing.Create(PricingModel.Hourly, 95m));
        loaded.Duration.Should().Be(Duration.Create(6, DurationUnit.Months));
        loaded.StartDate.Should().Be(new DateOnly(2026, 11, 1));
        loaded.EstimatedValue.Should().Be(95m * 8 * 120 * 0.8m);
        loaded.MonthlyRecurringValue.Should().BeNull();
    }

    [Fact]
    public async Task Create_ClosedStage_ReturnsStageNotOpen()
    {
        var result = await SendAsync<CreateOpportunity.Command, CreateOpportunity.Result>(
            new CreateOpportunity.Command("Migration", DefaultStages.Won));

        result.Error.Should().Be(OpportunityErrors.StageNotOpen);
    }

    [Fact]
    public async Task Create_UnknownReferences_ReturnFieldErrors()
    {
        (await SendAsync<CreateOpportunity.Command, CreateOpportunity.Result>(new CreateOpportunity.Command("M", Guid.CreateVersion7())))
            .Error.Should().Be(OpportunityErrors.StageNotFound);
        (await SendAsync<CreateOpportunity.Command, CreateOpportunity.Result>(new CreateOpportunity.Command("M", ClientOrganizationId: Guid.CreateVersion7())))
            .Error.Should().Be(OpportunityErrors.ClientNotFound);
        (await SendAsync<CreateOpportunity.Command, CreateOpportunity.Result>(new CreateOpportunity.Command("M", AgencyOrganizationId: Guid.CreateVersion7())))
            .Error.Should().Be(OpportunityErrors.AgencyNotFound);
        (await SendAsync<CreateOpportunity.Command, CreateOpportunity.Result>(new CreateOpportunity.Command("M", PrimaryContactId: Guid.CreateVersion7())))
            .Error.Should().Be(OpportunityErrors.ContactNotFound);
    }

    [Fact]
    public async Task Create_InvalidFields_ReturnsValidationErrors()
    {
        var result = await SendAsync<CreateOpportunity.Command, CreateOpportunity.Result>(new CreateOpportunity.Command(
            " ", Amount: -5m, Currency: "EURO", DurationValue: 3, Utilization: 120, RemotePercentage: -1));

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Keys.Should().BeEquivalentTo(
            "Title", "Amount", "Currency", "DurationUnit", "Utilization", "RemotePercentage");
    }

    [Fact]
    public async Task Update_ToLostWithoutReason_ReturnsLostReasonRequired()
    {
        var id = await CreateAsync(new CreateOpportunity.Command("Migration"));

        var result = await SendAsync<UpdateOpportunity.Command, UpdateOpportunity.Result>(Update(id, DefaultStages.Lost, null));

        result.Error.Should().Be(OpportunityErrors.LostReasonRequired);
    }

    [Fact]
    public async Task Update_ToLostWithReason_ClosesAndWritesStageChanged()
    {
        var id = await CreateAsync(new CreateOpportunity.Command("Migration"));
        Time.Advance(TimeSpan.FromDays(2));

        var result = await SendAsync<UpdateOpportunity.Command, UpdateOpportunity.Result>(Update(id, DefaultStages.Lost, LostReason.Price));

        result.IsSuccess.Should().BeTrue();
        await using var db = OpenDb();
        var stored = await db.Opportunities.SingleAsync(o => o.Id == id, Ct);
        stored.ClosedAt.Should().Be(Start.AddDays(2));
        stored.LostReason.Should().Be(LostReason.Price);
        var message = await db.OutboxMessages.SingleAsync(m => m.Type == "opportunity.stage_changed", Ct);
        message.Payload.Should().Contain("\"toStageStatus\": \"Lost\"");
    }

    [Fact]
    public async Task Update_UnknownIdOrStage_ReturnsNotFound()
    {
        var id = await CreateAsync(new CreateOpportunity.Command("Migration"));

        (await SendAsync<UpdateOpportunity.Command, UpdateOpportunity.Result>(Update(Guid.CreateVersion7(), DefaultStages.New, null)))
            .Error.Should().Be(OpportunityErrors.NotFound);
        (await SendAsync<UpdateOpportunity.Command, UpdateOpportunity.Result>(Update(id, Guid.CreateVersion7(), null)))
            .Error.Should().Be(OpportunityErrors.StageNotFound);
    }

    [Fact]
    public async Task Update_MissingTitle_ReturnsValidationError()
    {
        var id = await CreateAsync(new CreateOpportunity.Command("Migration"));

        var result = await SendAsync<UpdateOpportunity.Command, UpdateOpportunity.Result>(Update(id, DefaultStages.New, null) with { Title = "" });

        result.Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public async Task ArchiveAndRestore_Opportunity_TogglesIsArchived()
    {
        var id = await CreateAsync(new CreateOpportunity.Command("Migration"));

        (await SendAsync<ArchiveOpportunity.Command, ArchiveOpportunity.Result>(new ArchiveOpportunity.Command(id))).IsSuccess.Should().BeTrue();
        (await QueryAsync<GetOpportunity.Query, GetOpportunity.Result>(new GetOpportunity.Query(id))).Value.IsArchived.Should().BeTrue();

        (await SendAsync<RestoreOpportunity.Command, RestoreOpportunity.Result>(new RestoreOpportunity.Command(id))).IsSuccess.Should().BeTrue();
        (await QueryAsync<GetOpportunity.Query, GetOpportunity.Result>(new GetOpportunity.Query(id))).Value.IsArchived.Should().BeFalse();

        (await SendAsync<ArchiveOpportunity.Command, ArchiveOpportunity.Result>(new ArchiveOpportunity.Command(Guid.CreateVersion7())))
            .Error.Should().Be(OpportunityErrors.NotFound);
    }

    [Fact]
    public async Task GetOpportunity_UnknownId_ReturnsNotFound()
    {
        (await QueryAsync<GetOpportunity.Query, GetOpportunity.Result>(new GetOpportunity.Query(Guid.CreateVersion7())))
            .Error.Should().Be(OpportunityErrors.NotFound);
    }

    [Fact]
    public async Task GetOpportunityDefaults_Always_ReturnsStagesAndSettings()
    {
        var result = (await QueryAsync<GetOpportunityDefaults.Query, GetOpportunityDefaults.Result>(new GetOpportunityDefaults.Query())).Value;

        result.Stages.Select(s => s.Name).Should().Equal("Neu", "Beworben", "Im Gespräch", "Angebot", "Gewonnen", "Verloren");
        result.DefaultPricingModel.Should().Be(PricingModel.Hourly);
        result.DefaultCurrency.Should().Be("EUR");
        result.Valuation.Should().Be(ValuationSettings.Default);
    }

    private async Task<Guid> CreateAsync(CreateOpportunity.Command command)
    {
        var result = await SendAsync<CreateOpportunity.Command, CreateOpportunity.Result>(command);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        return result.Value.Id;
    }

    private async Task<Guid> CreateOrganizationAsync(string name, OrganizationType type) =>
        (await SendAsync<CreateOrganization.Command, CreateOrganization.Result>(new CreateOrganization.Command(name, type))).Value.Id;

    private static UpdateOpportunity.Command Update(Guid id, Guid stageId, LostReason? reason) =>
        new(id, "Migration", stageId, reason, null, null, null, null, null, null, null, null, null, null, null, null);
}
