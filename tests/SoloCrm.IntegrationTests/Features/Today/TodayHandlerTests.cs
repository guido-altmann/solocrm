using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Activities;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Application.Features.Opportunities;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Application.Features.Pipeline;
using SoloCrm.Application.Features.Tasks;
using SoloCrm.Application.Features.Timeline;
using SoloCrm.Application.Features.Today;
using SoloCrm.Domain.Activities;
using SoloCrm.Infrastructure.Persistence.Seeding;

namespace SoloCrm.IntegrationTests.Features.Today;

public sealed class TodayHandlerTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    [Fact]
    public async Task Get_Tasks_SplitsIntoOverdueTodayAndWithoutDueDate()
    {
        var today = DateOnly.FromDateTime(Start.Date);
        var overdue = await CreateTaskAsync("Überfällig", today.AddDays(-2));
        var dueToday = await CreateTaskAsync("Heute", today);
        var noDate = await CreateTaskAsync("Irgendwann", null);
        await CreateTaskAsync("Morgen", today.AddDays(1));
        var done = await CreateTaskAsync("Erledigt", today.AddDays(-1));
        await SendAsync<CompleteTask.Command, CompleteTask.Result>(new CompleteTask.Command(done));

        var result = await GetAsync();

        result.Today.Should().Be(today);
        result.Overdue.Select(t => t.Id).Should().Equal(overdue);
        result.DueToday.Select(t => t.Id).Should().Equal(dueToday);
        result.WithoutDueDate.Select(t => t.Id).Should().Equal(noDate);
    }

    [Fact]
    public async Task Get_AfterMidnightInBerlinButBeforeInUtc_UsesConfiguredTimeZone()
    {
        var yesterday = await CreateTaskAsync("Gestern fällig", new DateOnly(2026, 9, 29));
        var today = await CreateTaskAsync("Heute fällig", new DateOnly(2026, 9, 30));
        Time.SetUtcNow(new DateTimeOffset(2026, 9, 29, 22, 30, 0, TimeSpan.Zero)); // 00:30 in Berlin (CEST)

        var result = await GetAsync();

        result.Today.Should().Be(new DateOnly(2026, 9, 30));
        result.Overdue.Select(t => t.Id).Should().Equal(yesterday);
        result.DueToday.Select(t => t.Id).Should().Equal(today);
    }

    [Fact]
    public async Task Get_StaleOpportunities_AppliesThresholdAndEdgeCases()
    {
        var client = (await SendAsync<CreateOrganization.Command, CreateOrganization.Result>(new CreateOrganization.Command("Contoso"))).Value.Id;
        var withoutActivity = await CreateOpportunityAsync("Ohne Aktivität", client);
        var exactlySeven = await CreateOpportunityAsync("Genau 7 Tage");
        var sixDays = await CreateOpportunityAsync("6 Tage");
        var archived = await CreateOpportunityAsync("Archiviert");
        await SendAsync<ArchiveOpportunity.Command, ArchiveOpportunity.Result>(new ArchiveOpportunity.Command(archived));
        var won = await CreateOpportunityAsync("Gewonnen");
        await SendAsync<MoveOpportunity.Command, MoveOpportunity.Result>(new MoveOpportunity.Command(won, DefaultStages.Won));
        var contactOnly = await CreateOpportunityAsync("Aktivität nur am Kontakt");
        var contact = (await SendAsync<CreateContact.Command, CreateContact.Result>(new CreateContact.Command("Max", "Mustermann"))).Value.Id;

        Time.Advance(TimeSpan.FromDays(10));
        var now = Time.GetUtcNow();
        await LogAsync(exactlySeven, now.AddDays(-7));
        await LogAsync(sixDays, now.AddDays(-6));
        await SendAsync<LogActivity.Command, LogActivity.Result>(new LogActivity.Command(ActivityType.Call, "Anruf", ContactId: contact));

        var result = await GetAsync();

        result.StaleOpportunityDays.Should().Be(7);
        result.StaleOpportunities.Select(o => (o.Id, o.DaysSinceActivity)).Should().Equal(
            (withoutActivity, 10), (contactOnly, 10), (exactlySeven, 7));
        result.StaleOpportunities[0].Should().Match<GetToday.StaleOpportunity>(o =>
            o.ClientName == "Contoso" && o.StageName == "Neu" && o.LastActivityAt == Start);
        result.StaleOpportunities.Select(o => o.Id).Should().NotContain([sixDays, archived, won]);

        await Get<IAppSettings>().SetAsync(AppSettingKeys.StaleOpportunityDays, 8, Ct);
        (await GetAsync()).StaleOpportunities.Select(o => o.Id).Should().BeEquivalentTo([withoutActivity, contactOnly]);
    }

    [Fact]
    public async Task Get_RecentlyEdited_ReturnsNewestActiveRecordsAcrossTypes()
    {
        var contact = (await SendAsync<CreateContact.Command, CreateContact.Result>(new CreateContact.Command("Max", "Mustermann"))).Value.Id;
        Time.Advance(TimeSpan.FromMinutes(1));
        var organization = (await SendAsync<CreateOrganization.Command, CreateOrganization.Result>(new CreateOrganization.Command("Contoso"))).Value.Id;
        Time.Advance(TimeSpan.FromMinutes(1));
        var archived = await CreateOpportunityAsync("Archiviert");
        Time.Advance(TimeSpan.FromMinutes(1));
        await SendAsync<ArchiveOpportunity.Command, ArchiveOpportunity.Result>(new ArchiveOpportunity.Command(archived));
        var opportunity = await CreateOpportunityAsync("Migration");

        var result = await GetAsync();

        result.RecentlyEdited.Select(r => (r.Type, r.Id, r.Name)).Should().Equal(
            (TimelineRecordType.Opportunity, opportunity, "Migration"),
            (TimelineRecordType.Organization, organization, "Contoso"),
            (TimelineRecordType.Contact, contact, "Max Mustermann"));
    }

    [Fact]
    public async Task Get_RecentlyEdited_IsLimited()
    {
        for (var i = 0; i < GetToday.RecentlyEditedLimit + 3; i++)
        {
            await SendAsync<CreateOrganization.Command, CreateOrganization.Result>(new CreateOrganization.Command($"Org {i}"));
        }

        (await GetAsync()).RecentlyEdited.Should().HaveCount(GetToday.RecentlyEditedLimit);
    }

    [Fact]
    public async Task GetPipelineBoard_Cards_ShowDaysSinceLastDirectActivity()
    {
        var quiet = await CreateOpportunityAsync("Ruhig");
        var active = await CreateOpportunityAsync("Aktiv");
        Time.Advance(TimeSpan.FromDays(5));
        await LogAsync(active, Time.GetUtcNow().AddDays(-2));

        var board = (await QueryAsync<GetPipelineBoard.Query, GetPipelineBoard.Result>(new GetPipelineBoard.Query())).Value;

        var cards = board.Columns.SelectMany(c => c.Cards).ToDictionary(c => c.Id);
        cards[quiet].DaysSinceActivity.Should().Be(5);
        cards[quiet].LastActivityAt.Should().Be(Start);
        cards[active].DaysSinceActivity.Should().Be(2);
    }

    private async Task<GetToday.Result> GetAsync()
    {
        var result = await QueryAsync<GetToday.Query, GetToday.Result>(new GetToday.Query());
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private async Task<Guid> CreateTaskAsync(string title, DateOnly? dueDate) =>
        (await SendAsync<CreateTask.Command, CreateTask.Result>(new CreateTask.Command(title, dueDate))).Value.Id;

    private async Task<Guid> CreateOpportunityAsync(string title, Guid? client = null) =>
        (await SendAsync<CreateOpportunity.Command, CreateOpportunity.Result>(new CreateOpportunity.Command(title, ClientOrganizationId: client))).Value.Id;

    private async Task LogAsync(Guid opportunityId, DateTimeOffset occurredAt) =>
        (await SendAsync<LogActivity.Command, LogActivity.Result>(
            new LogActivity.Command(ActivityType.Call, "Anruf", OccurredAt: occurredAt, OpportunityId: opportunityId))).IsSuccess.Should().BeTrue();
}
