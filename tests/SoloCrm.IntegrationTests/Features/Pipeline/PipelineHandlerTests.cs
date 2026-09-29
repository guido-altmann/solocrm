using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Opportunities;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Application.Features.Pipeline;
using SoloCrm.Domain.Auditing;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Domain.Organizations;
using SoloCrm.Infrastructure.Persistence.Seeding;

namespace SoloCrm.IntegrationTests.Features.Pipeline;

public sealed class PipelineHandlerTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    [Fact]
    public async Task GetBoard_OpenRequests_GroupsCardsAndSumsPerCurrency()
    {
        var agency = (await SendAsync<CreateOrganization.Command, CreateOrganization.Result>(
            new CreateOrganization.Command("Hays", OrganizationType.Agency))).Value.Id;
        await CreateAsync(new CreateOpportunity.Command("A", AgencyOrganizationId: agency, PricingModel: PricingModel.Hourly,
            Amount: 100m, DurationValue: 1, DurationUnit: DurationUnit.Months));
        await CreateAsync(new CreateOpportunity.Command("B", PricingModel: PricingModel.Retainer, Amount: 2500m));
        await CreateAsync(new CreateOpportunity.Command("C", PricingModel: PricingModel.FixedPrice, Amount: 5000m, Currency: "CHF"));
        await CreateAsync(new CreateOpportunity.Command("D", DefaultStages.Offer, PricingModel: PricingModel.Hourly, Amount: 90m));
        var archived = await CreateAsync(new CreateOpportunity.Command("Archived"));
        await SendAsync<ArchiveOpportunity.Command, ArchiveOpportunity.Result>(new ArchiveOpportunity.Command(archived));

        var board = (await QueryAsync<GetPipelineBoard.Query, GetPipelineBoard.Result>(new GetPipelineBoard.Query())).Value;

        board.Columns.Select(c => c.Name).Should().Equal("Neu", "Beworben", "Im Gespräch", "Angebot", "Gewonnen", "Verloren");
        var first = board.Columns[0];
        first.Cards.Select(c => c.Title).Should().BeEquivalentTo("A", "B", "C");
        first.Cards.Single(c => c.Title == "A").Should().Match<GetPipelineBoard.Card>(c =>
            c.AgencyName == "Hays" && c.PricingDisplay == "100 €/h" && c.EstimatedValue == 16_000m);
        first.EstimatedValueTotals.Should().Equal(
            new GetPipelineBoard.MoneyTotal("CHF", 5_000m),
            new GetPipelineBoard.MoneyTotal("EUR", 46_000m));
        first.MonthlyRecurringTotals.Should().Equal(new GetPipelineBoard.MoneyTotal("EUR", 2_500m));
        var offer = board.Columns[3];
        offer.Cards.Should().ContainSingle().Which.EstimatedValue.Should().BeNull("an hourly rate without duration has no value");
        offer.EstimatedValueTotals.Should().BeEmpty();
    }

    [Fact]
    public async Task Move_ToOpenStage_ChangesStageWithAuditAndEvent()
    {
        var id = await CreateAsync(new CreateOpportunity.Command("Migration", DefaultStages.Applied));

        var result = await SendAsync<MoveOpportunity.Command, MoveOpportunity.Result>(new MoveOpportunity.Command(id, DefaultStages.InTalks));

        result.Value.Should().Be(new MoveOpportunity.Result(id, StageStatus.Open));
        await using var db = OpenDb();
        (await db.AuditEntries.SingleAsync(a => a.Action == AuditAction.Updated, Ct)).Changes
            .Should().Equal(new AuditChange("StageId", DefaultStages.Applied.ToString(), DefaultStages.InTalks.ToString()));
        var message = await db.OutboxMessages.SingleAsync(m => m.Type == "opportunity.stage_changed", Ct);
        message.Payload.Should().Be(
            $$"""{"toStageId": "{{DefaultStages.InTalks}}", "fromStageId": "{{DefaultStages.Applied}}", "opportunityId": "{{id}}", "toStageStatus": "Open"}""");
    }

    [Fact]
    public async Task Move_WonAndLost_LeaveActiveBoardAndAppearWithClosedFilter()
    {
        var won = await CreateAsync(new CreateOpportunity.Command("Won"));
        var lost = await CreateAsync(new CreateOpportunity.Command("Lost"));
        await CreateAsync(new CreateOpportunity.Command("Open"));

        (await SendAsync<MoveOpportunity.Command, MoveOpportunity.Result>(new MoveOpportunity.Command(won, DefaultStages.Won)))
            .Value.Status.Should().Be(StageStatus.Won);
        (await SendAsync<MoveOpportunity.Command, MoveOpportunity.Result>(new MoveOpportunity.Command(lost, DefaultStages.Lost, LostReason.Timing)))
            .Value.Status.Should().Be(StageStatus.Lost);

        var active = (await QueryAsync<GetPipelineBoard.Query, GetPipelineBoard.Result>(new GetPipelineBoard.Query())).Value;
        active.Columns.SelectMany(c => c.Cards).Select(c => c.Title).Should().Equal("Open");

        var all = (await QueryAsync<GetPipelineBoard.Query, GetPipelineBoard.Result>(new GetPipelineBoard.Query(IncludeClosed: true))).Value;
        all.Columns.Single(c => c.Status == StageStatus.Won).Cards.Should().ContainSingle()
            .Which.ClosedAt.Should().Be(Start);
        all.Columns.Single(c => c.Status == StageStatus.Lost).Cards.Should().ContainSingle()
            .Which.LostReason.Should().Be(LostReason.Timing);
    }

    [Fact]
    public async Task Move_ToLostWithoutReason_ReturnsLostReasonRequired()
    {
        var id = await CreateAsync(new CreateOpportunity.Command("Migration"));

        var result = await SendAsync<MoveOpportunity.Command, MoveOpportunity.Result>(new MoveOpportunity.Command(id, DefaultStages.Lost));

        result.Error.Should().Be(OpportunityErrors.LostReasonRequired);
        await using var db = OpenDb();
        (await db.Opportunities.SingleAsync(Ct)).StageId.Should().Be(DefaultStages.New);
    }

    [Fact]
    public async Task Move_InvalidInput_ReturnsErrors()
    {
        var id = await CreateAsync(new CreateOpportunity.Command("Migration"));

        (await SendAsync<MoveOpportunity.Command, MoveOpportunity.Result>(new MoveOpportunity.Command(Guid.CreateVersion7(), DefaultStages.Won)))
            .Error.Should().Be(OpportunityErrors.NotFound);
        (await SendAsync<MoveOpportunity.Command, MoveOpportunity.Result>(new MoveOpportunity.Command(id, Guid.CreateVersion7())))
            .Error.Should().Be(OpportunityErrors.StageNotFound);
        (await SendAsync<MoveOpportunity.Command, MoveOpportunity.Result>(new MoveOpportunity.Command(id, DefaultStages.Lost, (LostReason)42)))
            .Error.Should().BeOfType<ValidationError>();
    }

    private async Task<Guid> CreateAsync(CreateOpportunity.Command command)
    {
        var result = await SendAsync<CreateOpportunity.Command, CreateOpportunity.Result>(command);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        return result.Value.Id;
    }
}
