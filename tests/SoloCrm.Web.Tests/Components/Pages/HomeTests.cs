using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MudBlazor.Services;
using NSubstitute;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Tasks;
using SoloCrm.Application.Features.Timeline;
using SoloCrm.Application.Features.Today;
using SoloCrm.Web.Components.Pages;

namespace SoloCrm.Web.Tests.Components.Pages;

public sealed class HomeTests : BunitContext
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 29);

    private readonly IQueryHandler<GetToday.Query, GetToday.Result> _today =
        Substitute.For<IQueryHandler<GetToday.Query, GetToday.Result>>();

    public HomeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(_today);
        Services.AddSingleton(Substitute.For<ICommandHandler<CompleteTask.Command, CompleteTask.Result>>());
        Services.AddSingleton(Substitute.For<ICommandHandler<ReopenTask.Command, ReopenTask.Result>>());
        Services.AddSingleton(Substitute.For<ICommandHandler<DeleteTask.Command, DeleteTask.Result>>());
        Services.AddSingleton(new AppClock(new FakeTimeProvider(Now), TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")));
    }

    [Fact]
    public void Render_TodayResult_ShowsSectionsWithTasksStaleRequestsAndRecentRecords()
    {
        var opportunityId = Guid.CreateVersion7();
        Returns(new GetToday.Result(
            Today,
            7,
            [Task("Angebot nachfassen", Today.AddDays(-2), new RecordRef(opportunityId, "Migration Azure"))],
            [Task("Rückruf Max", Today)],
            [Task("Steuer", null)],
            [new GetToday.StaleOpportunity(opportunityId, "Migration Azure", "Beworben", "Contoso", Now.AddDays(-8), 8)],
            [new GetToday.RecentRecord(TimelineRecordType.Contact, Guid.CreateVersion7(), "Max Mustermann", Now)]));

        var home = Render<Home>();

        var overdue = home.Find("[data-section=overdue]");
        overdue.TextContent.Should().Contain("Überfällig (1)").And.Contain("Angebot nachfassen").And.Contain("27.09.2026");
        overdue.QuerySelector($"a[href='/opportunities/{opportunityId}']").Should().NotBeNull("tasks on „Heute“ link to their record");
        overdue.QuerySelector("h6")!.ClassList.Should().Contain("mud-error-text");
        home.Find("[data-section=due-today]").TextContent.Should().Contain("Heute fällig (1)").And.Contain("Rückruf Max");
        home.Find("[data-section=without-due-date]").TextContent.Should().Contain("Ohne Termin (1)");
        var stale = home.Find("[data-section=stale]");
        stale.TextContent.Should().Contain("mindestens 7 Tagen").And.Contain("Migration Azure").And.Contain("seit 8 Tagen");
        home.Find("[data-section=recent]").TextContent.Should().Contain("Max Mustermann");
        home.Markup.Should().Contain("Dienstag, 29. September 2026");
    }

    [Fact]
    public void Render_NothingDue_ShowsEmptyTexts()
    {
        Returns(new GetToday.Result(Today, 7, [], [], [], [], []));

        var home = Render<Home>();

        home.Find("[data-section=overdue]").TextContent.Should().Contain("Nichts überfällig.");
        home.Find("[data-section=overdue] h6").ClassList.Should().NotContain("mud-error-text");
        home.Find("[data-section=due-today]").TextContent.Should().Contain("Für heute ist nichts fällig.");
        home.Find("[data-section=stale]").TextContent.Should().Contain("Alle Anfragen sind in Bewegung.");
    }

    private void Returns(GetToday.Result result) =>
        _today.Handle(Arg.Any<GetToday.Query>(), Arg.Any<CancellationToken>()).Returns(Result<GetToday.Result>.Success(result));

    private static TaskSummary Task(string title, DateOnly? due, RecordRef? opportunity = null) =>
        new(Guid.CreateVersion7(), title, due, null, null, null, opportunity);
}
