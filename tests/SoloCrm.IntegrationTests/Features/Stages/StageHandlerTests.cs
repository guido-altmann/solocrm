using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Opportunities;
using SoloCrm.Application.Features.Pipeline;
using SoloCrm.Application.Features.Stages;
using SoloCrm.Domain.Auditing;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Infrastructure.Persistence.Seeding;

namespace SoloCrm.IntegrationTests.Features.Stages;

public sealed class StageHandlerTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    [Fact]
    public async Task Create_ValidCommand_AppendsStage()
    {
        var result = await SendAsync<CreateStage.Command, CreateStage.Result>(new CreateStage.Command(" Verhandlung "));

        var stages = await ListAsync();
        stages[^1].Should().Match<GetStages.Item>(s =>
            s.Id == result.Value.Id && s.Name == "Verhandlung" && s.Status == StageStatus.Open && s.SortOrder == 7);
    }

    [Fact]
    public async Task Create_InvalidCommand_ReturnsValidationError()
    {
        var result = await SendAsync<CreateStage.Command, CreateStage.Result>(new CreateStage.Command(" ", (StageStatus)9));

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Keys.Should().BeEquivalentTo("Name", "Status");
    }

    [Fact]
    public async Task Rename_ExistingStage_ChangesNameWithAudit()
    {
        (await SendAsync<RenameStage.Command, RenameStage.Result>(new RenameStage.Command(DefaultStages.Applied, "Bewerbung raus")))
            .IsSuccess.Should().BeTrue();

        (await ListAsync()).Single(s => s.Id == DefaultStages.Applied).Name.Should().Be("Bewerbung raus");
        await using var db = OpenDb();
        (await db.AuditEntries.SingleAsync(Ct)).Changes.Should().Equal(new AuditChange("Name", "Beworben", "Bewerbung raus"));
    }

    [Fact]
    public async Task Rename_InvalidInput_ReturnsErrors()
    {
        (await SendAsync<RenameStage.Command, RenameStage.Result>(new RenameStage.Command(DefaultStages.New, "")))
            .Error.Should().BeOfType<ValidationError>();
        (await SendAsync<RenameStage.Command, RenameStage.Result>(new RenameStage.Command(Guid.CreateVersion7(), "X")))
            .Error.Should().Be(StageErrors.NotFound);
    }

    [Fact]
    public async Task Reorder_AllStages_AppliesNewOrder()
    {
        var order = new[] { DefaultStages.Applied, DefaultStages.New, DefaultStages.InTalks, DefaultStages.Offer, DefaultStages.Lost, DefaultStages.Won };

        (await SendAsync<ReorderStages.Command, ReorderStages.Result>(new ReorderStages.Command(order))).IsSuccess.Should().BeTrue();

        (await ListAsync()).Select(s => s.Id).Should().Equal(order);
        (await SendAsync<CreateOpportunity.Command, CreateOpportunity.Result>(new CreateOpportunity.Command("First open stage")))
            .IsSuccess.Should().BeTrue();
        await using var db = OpenDb();
        (await db.Opportunities.SingleAsync(Ct)).StageId.Should().Be(DefaultStages.Applied);
    }

    [Fact]
    public async Task Reorder_IncompleteList_ReturnsError()
    {
        var result = await SendAsync<ReorderStages.Command, ReorderStages.Result>(
            new ReorderStages.Command([DefaultStages.New, DefaultStages.New, DefaultStages.InTalks, DefaultStages.Offer, DefaultStages.Won, DefaultStages.Lost]));

        result.Error.Should().Be(StageErrors.IncompleteOrder);
    }

    [Fact]
    public async Task Delete_EmptyStage_RemovesItWithDeletedAudit()
    {
        var result = await SendAsync<DeleteStage.Command, DeleteStage.Result>(new DeleteStage.Command(DefaultStages.Offer));

        result.Value.ReassignedCount.Should().Be(0);
        (await ListAsync()).Should().NotContain(s => s.Id == DefaultStages.Offer);
        await using var db = OpenDb();
        (await db.AuditEntries.SingleAsync(Ct)).Action.Should().Be(AuditAction.Deleted);
    }

    [Fact]
    public async Task Delete_StageWithOpportunities_RequiresTargetAndReassigns()
    {
        var open = await CreateOpportunityAsync("Open", DefaultStages.Applied);
        var archived = await CreateOpportunityAsync("Archived", DefaultStages.Applied);
        await SendAsync<ArchiveOpportunity.Command, ArchiveOpportunity.Result>(new ArchiveOpportunity.Command(archived));

        (await SendAsync<DeleteStage.Command, DeleteStage.Result>(new DeleteStage.Command(DefaultStages.Applied)))
            .Error.Should().Be(StageErrors.TargetRequired);
        (await SendAsync<DeleteStage.Command, DeleteStage.Result>(new DeleteStage.Command(DefaultStages.Applied, DefaultStages.Won)))
            .Error.Should().Be(StageErrors.InvalidTarget);
        (await SendAsync<DeleteStage.Command, DeleteStage.Result>(new DeleteStage.Command(DefaultStages.Applied, DefaultStages.Applied)))
            .Error.Should().Be(StageErrors.InvalidTarget);

        var result = await SendAsync<DeleteStage.Command, DeleteStage.Result>(new DeleteStage.Command(DefaultStages.Applied, DefaultStages.InTalks));

        result.Value.ReassignedCount.Should().Be(2);
        await using var db = OpenDb();
        (await db.Opportunities.Select(o => o.StageId).ToListAsync(Ct)).Should().AllBeEquivalentTo(DefaultStages.InTalks);
        (await db.Stages.AnyAsync(s => s.Id == DefaultStages.Applied, Ct)).Should().BeFalse();
        (await db.OutboxMessages.CountAsync(m => m.Type == "opportunity.stage_changed", Ct)).Should().Be(2);
        (await db.AuditEntries.CountAsync(a => a.EntityId == open && a.Action == AuditAction.Updated, Ct)).Should().Be(1);
    }

    [Fact]
    public async Task Delete_LostStageWithLostOpportunity_KeepsReasonInTarget()
    {
        var secondLost = (await SendAsync<CreateStage.Command, CreateStage.Result>(new CreateStage.Command("Abgesagt", StageStatus.Lost))).Value.Id;
        var id = await CreateOpportunityAsync("Lost", DefaultStages.New);
        await SendAsync<MoveOpportunity.Command, MoveOpportunity.Result>(new MoveOpportunity.Command(id, DefaultStages.Lost, LostReason.Price));

        (await SendAsync<DeleteStage.Command, DeleteStage.Result>(new DeleteStage.Command(DefaultStages.Lost, secondLost)))
            .IsSuccess.Should().BeTrue();

        await using var db = OpenDb();
        var stored = await db.Opportunities.SingleAsync(Ct);
        stored.StageId.Should().Be(secondLost);
        stored.LostReason.Should().Be(LostReason.Price);
        stored.ClosedAt.Should().Be(Start);
    }

    [Theory]
    [InlineData("0199a000-0000-7000-8000-000000000005")]
    [InlineData("0199a000-0000-7000-8000-000000000006")]
    public async Task Delete_LastWonOrLostStage_ReturnsLastOfStatus(string stageId)
    {
        var result = await SendAsync<DeleteStage.Command, DeleteStage.Result>(new DeleteStage.Command(Guid.Parse(stageId)));

        result.Error.Code.Should().Be("Stage.LastOfStatus");
        (await ListAsync()).Should().HaveCount(6);
    }

    [Fact]
    public async Task Delete_LastOpenStage_ReturnsLastOfStatus()
    {
        foreach (var id in new[] { DefaultStages.New, DefaultStages.Applied, DefaultStages.InTalks })
        {
            (await SendAsync<DeleteStage.Command, DeleteStage.Result>(new DeleteStage.Command(id))).IsSuccess.Should().BeTrue();
        }

        (await SendAsync<DeleteStage.Command, DeleteStage.Result>(new DeleteStage.Command(DefaultStages.Offer)))
            .Error.Should().Be(StageErrors.LastOfStatus(StageStatus.Open));
    }

    [Fact]
    public async Task Delete_UnknownStage_ReturnsNotFound()
    {
        (await SendAsync<DeleteStage.Command, DeleteStage.Result>(new DeleteStage.Command(Guid.CreateVersion7())))
            .Error.Should().Be(StageErrors.NotFound);
    }

    [Fact]
    public async Task GetStages_WithOpportunities_CountsAssignedRequests()
    {
        await CreateOpportunityAsync("A", DefaultStages.New);
        await CreateOpportunityAsync("B", DefaultStages.New);

        (await ListAsync()).Single(s => s.Id == DefaultStages.New).OpportunityCount.Should().Be(2);
    }

    private async Task<IReadOnlyList<GetStages.Item>> ListAsync() =>
        (await QueryAsync<GetStages.Query, GetStages.Result>(new GetStages.Query())).Value.Items;

    private async Task<Guid> CreateOpportunityAsync(string title, Guid stageId) =>
        (await SendAsync<CreateOpportunity.Command, CreateOpportunity.Result>(new CreateOpportunity.Command(title, stageId))).Value.Id;
}
