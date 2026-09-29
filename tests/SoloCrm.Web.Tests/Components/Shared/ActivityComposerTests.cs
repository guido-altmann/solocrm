using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MudBlazor.Services;
using NSubstitute;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Activities;
using SoloCrm.Domain.Activities;
using SoloCrm.Domain.Common;
using SoloCrm.Web.Components.Shared;

namespace SoloCrm.Web.Tests.Components.Shared;

public sealed class ActivityComposerTests : BunitContext
{
    // 12:00 in Berlin (CEST, UTC+2).
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    private readonly Guid _contactId = Guid.CreateVersion7();

    private readonly ICommandHandler<LogActivity.Command, LogActivity.Result> _log =
        Substitute.For<ICommandHandler<LogActivity.Command, LogActivity.Result>>();

    public ActivityComposerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(_log);
        Services.AddSingleton(new AppClock(new FakeTimeProvider(Now), TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")));
        _log.Handle(Arg.Any<LogActivity.Command>(), Arg.Any<CancellationToken>())
            .Returns(Result<LogActivity.Result>.Success(new LogActivity.Result(Guid.CreateVersion7())));
    }

    [Fact]
    public async Task Save_NoteWithoutDate_SendsNoteForNow()
    {
        Guid? logged = null;
        var composer = RenderComposer(id => logged = id);

        composer.Find("textarea").Input("Kurz telefoniert");
        await ClickSaveAsync(composer);

        await _log.Received(1).Handle(
            new LogActivity.Command(ActivityType.Note, "Kurz telefoniert", null, null, _contactId),
            Arg.Any<CancellationToken>());
        logged.Should().NotBeNull();
        composer.WaitForAssertion(() => composer.Find("textarea").GetAttribute("value").Should().BeNullOrEmpty());
    }

    [Fact]
    public async Task Save_CallBackdated_SendsTypeAndLocalTimeAsUtc()
    {
        var composer = RenderComposer();

        composer.FindAll(".mud-chip").Single(c => c.TextContent.Contains("Anruf", StringComparison.Ordinal)).Click();
        composer.Find("textarea").Input("Budget geklärt");
        var inputs = composer.FindAll("input");
        inputs.Single(i => i.GetAttribute("placeholder") == "Heute").Change("27.09.2026");
        composer.FindAll("input").Single(i => i.GetAttribute("placeholder") == "Jetzt").Change("14:30");
        await ClickSaveAsync(composer);

        await _log.Received(1).Handle(
            Arg.Is<LogActivity.Command>(c =>
                c.Type == ActivityType.Call
                && c.OccurredAt == new DateTimeOffset(2026, 9, 27, 12, 30, 0, TimeSpan.Zero)
                && c.ContactId == _contactId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_ExtraLinkSelected_AddsContactOfOpportunity()
    {
        var opportunityId = Guid.CreateVersion7();
        var composer = Render<ActivityComposer>(p => p
            .Add(c => c.LinkedTo, new LinkedRecords(OpportunityId: opportunityId))
            .Add(c => c.ExtraLinks, [new ActivityLinkOption("Auch bei Max Mustermann", ContactId: _contactId)]));

        composer.Find("textarea").Input("Absprache");
        composer.Find("input[type=checkbox]").Change(true);
        await ClickSaveAsync(composer);

        await _log.Received(1).Handle(
            Arg.Is<LogActivity.Command>(c => c.OpportunityId == opportunityId && c.ContactId == _contactId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_ValidationError_ShowsMessageAtBody()
    {
        _log.Handle(Arg.Any<LogActivity.Command>(), Arg.Any<CancellationToken>())
            .Returns(Result<LogActivity.Result>.Failure(new ValidationError(new Dictionary<string, string[]>
            {
                ["Body"] = ["Bitte einen Text eingeben."],
            })));
        var composer = RenderComposer();

        await ClickSaveAsync(composer);

        composer.WaitForAssertion(() => composer.Markup.Should().Contain("Bitte einen Text eingeben."));
    }

    private IRenderedComponent<ActivityComposer> RenderComposer(Action<Guid>? onLogged = null) =>
        Render<ActivityComposer>(p => p
            .Add(c => c.LinkedTo, new LinkedRecords(ContactId: _contactId))
            .Add(c => c.OnLogged, id => onLogged?.Invoke(id)));

    private static Task ClickSaveAsync(IRenderedComponent<ActivityComposer> composer) =>
        composer.FindAll("button").Single(b => b.TextContent.Contains("speichern", StringComparison.Ordinal)).ClickAsync(new());
}
