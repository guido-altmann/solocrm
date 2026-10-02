using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Opportunities;
using SoloCrm.Application.Features.Pipeline;
using SoloCrm.Application.Features.Timeline;
using SoloCrm.Application.Features.Today;
using SoloCrm.Domain.Auditing;

namespace SoloCrm.IntegrationTests.Features.Opportunities;

/// <summary>Date a request came in, for requests recorded later (iteration 6 decision 11).</summary>
public sealed class ReceivedOnHandlerTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    // Start is 2026-09-29 08:00 UTC, i.e. 10:00 in Berlin.
    private static readonly DateOnly Today = new(2026, 9, 29);

    [Fact]
    public async Task Create_WithoutReceivedOn_UsesTodayInConfiguredTimeZone()
    {
        Time.SetUtcNow(new DateTimeOffset(2026, 9, 29, 22, 30, 0, TimeSpan.Zero));

        var id = await CreateAsync(new CreateOpportunity.Command("Migration"));

        (await GetAsync(id)).ReceivedOn.Should().Be(new DateOnly(2026, 9, 30));
    }

    [Fact]
    public async Task Create_EarlierReceivedOn_KeepsDateAndTechnicalCreatedAt()
    {
        var id = await CreateAsync(new CreateOpportunity.Command("Migration", ReceivedOn: new DateOnly(2026, 8, 3)));

        (await GetAsync(id)).ReceivedOn.Should().Be(new DateOnly(2026, 8, 3));
        await using var db = OpenDb();
        (await db.Opportunities.SingleAsync(o => o.Id == id, Ct)).CreatedAt.Should().Be(Start);
    }

    [Fact]
    public async Task Create_FutureReceivedOn_ReturnsValidationError()
    {
        var result = await SendAsync<CreateOpportunity.Command, CreateOpportunity.Result>(
            new CreateOpportunity.Command("Migration", ReceivedOn: Today.AddDays(1)));

        result.Error.Should().BeOfType<ValidationError>()
            .Which.Errors.Should().ContainKey(nameof(IOpportunityFields.ReceivedOn));
    }

    [Fact]
    public async Task Update_ReceivedOn_ChangesDateAndWritesAudit()
    {
        var id = await CreateAsync(new CreateOpportunity.Command("Migration"));
        var o = await GetAsync(id);

        var result = await SendAsync<UpdateOpportunity.Command, UpdateOpportunity.Result>(new UpdateOpportunity.Command(
            id, o.Title, o.StageId, null, null, null, null, null, null, null, null, null, null, null, null, null, new DateOnly(2026, 9, 1)));

        result.IsSuccess.Should().BeTrue();
        (await GetAsync(id)).ReceivedOn.Should().Be(new DateOnly(2026, 9, 1));
        await using var db = OpenDb();
        var audit = await db.AuditEntries.SingleAsync(a => a.EntityId == id && a.Action == AuditAction.Updated, Ct);
        audit.Changes.Should().Equal(new AuditChange("ReceivedOn", "2026-09-29", "2026-09-01"));
    }

    [Fact]
    public async Task Update_WithoutReceivedOn_KeepsDate()
    {
        var id = await CreateAsync(new CreateOpportunity.Command("Migration", ReceivedOn: new DateOnly(2026, 8, 3)));
        var o = await GetAsync(id);

        await SendAsync<UpdateOpportunity.Command, UpdateOpportunity.Result>(new UpdateOpportunity.Command(
            id, "Migration Azure", o.StageId, null, null, null, null, null, null, null, null, null, null, null, null, null));

        (await GetAsync(id)).ReceivedOn.Should().Be(new DateOnly(2026, 8, 3));
    }

    [Fact]
    public async Task Update_FutureReceivedOn_ReturnsValidationError()
    {
        var id = await CreateAsync(new CreateOpportunity.Command("Migration"));
        var o = await GetAsync(id);

        var result = await SendAsync<UpdateOpportunity.Command, UpdateOpportunity.Result>(new UpdateOpportunity.Command(
            id, o.Title, o.StageId, null, null, null, null, null, null, null, null, null, null, null, null, null, Today.AddDays(1)));

        result.Error.Should().BeOfType<ValidationError>()
            .Which.Errors.Should().ContainKey(nameof(IOpportunityFields.ReceivedOn));
    }

    [Fact]
    public async Task GetOpportunities_LateRecordedRequest_IsSortedByReceivedOn()
    {
        var older = await CreateAsync(new CreateOpportunity.Command("Älter, später erfasst", ReceivedOn: Today.AddDays(-20)));
        var newer = await CreateAsync(new CreateOpportunity.Command("Neuer", ReceivedOn: Today.AddDays(-2)));
        Time.Advance(TimeSpan.FromMinutes(5));
        var latest = await CreateAsync(new CreateOpportunity.Command("Heute, zuletzt erfasst"));

        var items = (await QueryAsync<GetOpportunities.Query, GetOpportunities.Result>(new GetOpportunities.Query())).Value.Items;

        items.Select(i => i.Id).Should().Equal(latest, newer, older);
        items[2].ReceivedOn.Should().Be(Today.AddDays(-20));
    }

    [Fact]
    public async Task GetToday_LateRecordedRequestWithoutActivity_IsStaleSinceReceivedOn()
    {
        var late = await CreateAsync(new CreateOpportunity.Command("Später erfasst", ReceivedOn: Today.AddDays(-9)));
        var recent = await CreateAsync(new CreateOpportunity.Command("Vor 6 Tagen", ReceivedOn: Today.AddDays(-6)));

        var today = (await QueryAsync<GetToday.Query, GetToday.Result>(new GetToday.Query())).Value;

        today.StaleOpportunities.Select(o => (o.Id, o.DaysSinceActivity)).Should().Equal((late, 9));
        today.StaleOpportunities.Select(o => o.Id).Should().NotContain(recent);
    }

    [Fact]
    public async Task GetPipelineBoard_LateRecordedRequestWithoutActivity_CountsDaysSinceReceivedOn()
    {
        var late = await CreateAsync(new CreateOpportunity.Command("Später erfasst", ReceivedOn: Today.AddDays(-4)));

        var board = (await QueryAsync<GetPipelineBoard.Query, GetPipelineBoard.Result>(new GetPipelineBoard.Query())).Value;

        board.Columns.SelectMany(c => c.Cards).Single(c => c.Id == late).DaysSinceActivity.Should().Be(4);
    }

    [Fact]
    public async Task GetTimeline_LateRecordedRequest_CreatedEntryNamesReceivedOn()
    {
        var late = await CreateAsync(new CreateOpportunity.Command("Später erfasst", ReceivedOn: new DateOnly(2026, 9, 1)));
        var sameDay = await CreateAsync(new CreateOpportunity.Command("Heute erfasst"));

        var lateEntry = await CreatedEntryAsync(late);
        var sameDayEntry = await CreatedEntryAsync(sameDay);

        lateEntry.ReceivedOn.Should().Be(new DateOnly(2026, 9, 1));
        lateEntry.OccurredAt.Should().Be(Start);
        sameDayEntry.ReceivedOn.Should().BeNull();
    }

    private async Task<GetTimeline.Entry> CreatedEntryAsync(Guid id) =>
        (await QueryAsync<GetTimeline.Query, GetTimeline.Result>(new GetTimeline.Query(TimelineRecordType.Opportunity, id)))
            .Value.Entries.Single(e => e.Kind == TimelineEntryKind.Created);

    private async Task<Guid> CreateAsync(CreateOpportunity.Command command)
    {
        var result = await SendAsync<CreateOpportunity.Command, CreateOpportunity.Result>(command);
        result.IsSuccess.Should().BeTrue();
        return result.Value.Id;
    }

    private async Task<GetOpportunity.Result> GetAsync(Guid id) =>
        (await QueryAsync<GetOpportunity.Query, GetOpportunity.Result>(new GetOpportunity.Query(id))).Value;
}
