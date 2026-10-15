using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.CalendarFeeds;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Application.Features.Tasks;

namespace SoloCrm.IntegrationTests.Features.CalendarFeeds;

/// <summary>ICS feed of the tasks (SOL-6): the token is stored hashed, replaced tokens stop working.</summary>
public sealed class CalendarFeedHandlerTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    private const string BaseUrl = "https://crm.example.de";

    private static readonly DateOnly Due = new(2026, 10, 20);

    [Fact]
    public async Task Generate_NoFeed_StoresOnlyTheHash()
    {
        var generated = await GenerateAsync();

        await using var db = OpenDb();
        var stored = await db.CalendarFeeds.SingleAsync(Ct);
        stored.TokenHash.Should().Be(CalendarFeedToken.Hash(generated.Token));
        stored.TokenHash.Should().NotContain(generated.Token);
    }

    [Fact]
    public async Task Generate_ExistingFeed_ReplacesItAndInvalidatesTheOldToken()
    {
        var old = await GenerateAsync();

        var current = await GenerateAsync();

        await using var db = OpenDb();
        (await db.CalendarFeeds.CountAsync(Ct)).Should().Be(1);
        (await FeedAsync(old.Token)).Error.Should().Be(CalendarFeedErrors.Invalid);
        (await FeedAsync(current.Token)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Revoke_ExistingFeed_RejectsTheTokenAfterwards()
    {
        var generated = await GenerateAsync();

        var revoked = await SendAsync<RevokeCalendarFeed.Command, RevokeCalendarFeed.Result>(new RevokeCalendarFeed.Command());

        revoked.IsSuccess.Should().BeTrue();
        (await FeedAsync(generated.Token)).Error.Should().Be(CalendarFeedErrors.Invalid);
        (await QueryAsync<GetCalendarFeed.Query, GetCalendarFeed.Result>(new GetCalendarFeed.Query())).Value.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Revoke_NoFeed_ReturnsNotFound()
    {
        var result = await SendAsync<RevokeCalendarFeed.Command, RevokeCalendarFeed.Result>(new RevokeCalendarFeed.Command());

        result.Error.Should().Be(CalendarFeedErrors.NotFound);
    }

    [Fact]
    public async Task GetFeed_NoFeedYet_ReportsDisabled()
    {
        var result = await QueryAsync<GetCalendarFeed.Query, GetCalendarFeed.Result>(new GetCalendarFeed.Query());

        result.Value.Should().Be(new GetCalendarFeed.Result(false, null, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("garbage")]
    [InlineData("0123456789012345678901234567890123456789012")]
    public async Task Feed_UnknownOrMalformedToken_ReturnsInvalidWithoutRecordingUse(string? token)
    {
        await GenerateAsync();

        var result = await FeedAsync(token);

        result.Error.Should().Be(CalendarFeedErrors.Invalid);
        (await QueryAsync<GetCalendarFeed.Query, GetCalendarFeed.Result>(new GetCalendarFeed.Query())).Value.LastUsedAt.Should().BeNull();
    }

    [Fact]
    public async Task Feed_OpenAndCompletedTasks_ContainsOpenAndRecentlyCompletedOnly()
    {
        var generated = await GenerateAsync();
        await CreateTaskAsync("Angebot nachfassen", Due);
        await CreateTaskAsync("Ohne Termin", null);
        var recent = await CreateTaskAsync("Kürzlich erledigt", Due);
        var old = await CreateTaskAsync("Lange erledigt", Due);
        await SendAsync<CompleteTask.Command, CompleteTask.Result>(new CompleteTask.Command(old));
        Time.Advance(TimeSpan.FromDays(GetTaskFeed.CompletedRetentionDays + 1));
        await SendAsync<CompleteTask.Command, CompleteTask.Result>(new CompleteTask.Command(recent));

        var content = (await FeedAsync(generated.Token)).Value.Content;

        content.Should().Contain("BEGIN:VCALENDAR").And.Contain("X-WR-CALNAME:" + GetTaskFeed.CalendarName);
        content.Should().Contain("SUMMARY:Angebot nachfassen").And.Contain("SUMMARY:✓ Kürzlich erledigt");
        content.Should().NotContain("Lange erledigt", "completed more than the retention period ago")
            .And.NotContain("Ohne Termin", "a calendar needs a date");
        content.Should().Contain("DTSTART;VALUE=DATE:20261020").And.Contain("DTEND;VALUE=DATE:20261021");
        content.Should().Contain("TRANSP:TRANSPARENT").And.NotContain("VTODO");
        content.Split("BEGIN:VEVENT").Length.Should().Be(3, "two tasks are in the feed");
    }

    [Fact]
    public async Task Feed_TaskWithSpecialCharacters_EscapesTitle()
    {
        var generated = await GenerateAsync();
        await CreateTaskAsync("Angebot; Preis, Zeitplan", Due);

        var content = (await FeedAsync(generated.Token)).Value.Content;

        content.Should().Contain(@"SUMMARY:Angebot\; Preis\, Zeitplan");
    }

    [Fact]
    public async Task Feed_LinkedTask_ContainsLinkToTheRecord()
    {
        var generated = await GenerateAsync();
        var contact = await SendAsync<CreateContact.Command, CreateContact.Result>(new CreateContact.Command("Max", "Mustermann", null, null));
        await SendAsync<CreateTask.Command, CreateTask.Result>(new CreateTask.Command("Rückruf", Due, ContactId: contact.Value.Id));

        var content = (await FeedAsync(generated.Token)).Value.Content;

        content.Should().Contain($"URL:{BaseUrl}/contacts/{contact.Value.Id}");
    }

    [Fact]
    public async Task Feed_Retrieved_RecordsUseThrottled()
    {
        var generated = await GenerateAsync();

        await FeedAsync(generated.Token);
        Time.Advance(TimeSpan.FromSeconds(30));
        await FeedAsync(generated.Token);

        (await QueryAsync<GetCalendarFeed.Query, GetCalendarFeed.Result>(new GetCalendarFeed.Query())).Value
            .Should().Match<GetCalendarFeed.Result>(r => r.IsEnabled && r.LastUsedAt == Start);
    }

    private async Task<GenerateCalendarFeed.Result> GenerateAsync() =>
        (await SendAsync<GenerateCalendarFeed.Command, GenerateCalendarFeed.Result>(new GenerateCalendarFeed.Command())).Value;

    private Task<Result<GetTaskFeed.Result>> FeedAsync(string? token) =>
        QueryAsync<GetTaskFeed.Query, GetTaskFeed.Result>(new GetTaskFeed.Query(token, BaseUrl));

    private async Task<Guid> CreateTaskAsync(string title, DateOnly? due) =>
        (await SendAsync<CreateTask.Command, CreateTask.Result>(new CreateTask.Command(title, due))).Value.Id;
}
