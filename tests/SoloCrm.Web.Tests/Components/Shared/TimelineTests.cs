using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MudBlazor.Services;
using NSubstitute;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Activities;
using SoloCrm.Application.Features.Timeline;
using SoloCrm.Domain.Activities;
using SoloCrm.Domain.Common;
using SoloCrm.Web.Components.Shared;
using Kind = SoloCrm.Application.Features.Timeline.TimelineEntryKind;

namespace SoloCrm.Web.Tests.Components.Shared;

public sealed class TimelineTests : BunitContext
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    private readonly Guid _organizationId = Guid.CreateVersion7();

    private readonly IQueryHandler<GetTimeline.Query, GetTimeline.Result> _timeline =
        Substitute.For<IQueryHandler<GetTimeline.Query, GetTimeline.Result>>();

    public TimelineTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(_timeline);
        Services.AddSingleton(Substitute.For<ICommandHandler<LogActivity.Command, LogActivity.Result>>());
        Services.AddSingleton(Substitute.For<ICommandHandler<DeleteActivity.Command, DeleteActivity.Result>>());
        Services.AddSingleton(new AppClock(new FakeTimeProvider(Now), TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")));
    }

    [Fact]
    public void Render_Entries_ShowsChangesViaLinksAndLocalTimes()
    {
        var contactId = Guid.CreateVersion7();
        var stageChange = Entry(Kind.Updated, Now, changes: [new GetTimeline.Change("Phase", "Beworben", "Im Gespräch")]);
        var call = Entry(Kind.Activity, Now.AddHours(-1), via: new GetTimeline.Via(TimelineRecordType.Contact, contactId, "Max Mustermann")) with
        {
            ActivityType = ActivityType.Call,
            Subject = "Erstgespräch",
            Body = "**Budget** geklärt",
        };
        Returns(null, new GetTimeline.Result([stageChange, call], null));

        var timeline = RenderTimeline();

        var entries = timeline.FindAll(".timeline-entry");
        entries.Should().HaveCount(2);
        entries[0].TextContent.Should().Contain("Geändert").And.Contain("Phase: Beworben → Im Gespräch").And.Contain("29.09.2026, 12:00");
        entries[1].TextContent.Should().Contain("Anruf").And.Contain("Erstgespräch");
        entries[1].QuerySelector(".timeline-body strong")!.TextContent.Should().Be("Budget");
        entries[1].QuerySelector($".timeline-via a[href='/contacts/{contactId}']")!.TextContent.Should().Be("Max Mustermann");
        timeline.Markup.Should().NotContain("Mehr laden");
    }

    [Fact]
    public void Render_CreatedOfLateRecordedRequest_ShowsReceivedOn()
    {
        var late = Entry(Kind.Created, Now) with { ReceivedOn = new DateOnly(2026, 9, 1) };
        var sameDay = Entry(Kind.Created, Now.AddHours(-1));
        Returns(null, new GetTimeline.Result([late, sameDay], null));

        var timeline = RenderTimeline();

        var entries = timeline.FindAll(".timeline-entry");
        entries[0].TextContent.Should().Contain("Angelegt").And.Contain("Eingegangen am 01.09.2026");
        entries[1].TextContent.Should().NotContain("Eingegangen");
    }

    [Fact]
    public async Task LoadMore_NextCursor_AppendsOlderEntries()
    {
        var newer = Entry(Kind.Created, Now);
        var older = Entry(Kind.Archived, Now.AddDays(-1));
        Returns(null, new GetTimeline.Result([newer], newer.Position));
        Returns(newer.Position, new GetTimeline.Result([older], null));
        var timeline = RenderTimeline();

        await timeline.FindAll("button").Single(b => b.TextContent.Contains("Mehr laden", StringComparison.Ordinal)).ClickAsync(new());

        timeline.FindAll(".timeline-entry").Select(e => e.GetAttribute("data-kind")).Should().Equal("Created", "Archived");
        timeline.Markup.Should().NotContain("Mehr laden");
    }

    private IRenderedComponent<Timeline> RenderTimeline() => Render<Timeline>(p => p
        .Add(t => t.RecordType, TimelineRecordType.Organization)
        .Add(t => t.RecordId, _organizationId)
        .Add(t => t.LinkedTo, new LinkedRecords(OrganizationId: _organizationId)));

    private void Returns(GetTimeline.Cursor? before, GetTimeline.Result result) =>
        _timeline.Handle(new GetTimeline.Query(TimelineRecordType.Organization, _organizationId, before), Arg.Any<CancellationToken>())
            .Returns(Result<GetTimeline.Result>.Success(result));

    private static GetTimeline.Entry Entry(
        Kind kind, DateTimeOffset at, GetTimeline.Via? via = null, IReadOnlyList<GetTimeline.Change>? changes = null)
    {
        var id = Guid.CreateVersion7();
        return new GetTimeline.Entry(kind, id, at, via, new GetTimeline.Cursor(at, TimelineSource.Audit, id), Changes: changes);
    }
}
