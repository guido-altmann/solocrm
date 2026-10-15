using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.CalendarFeeds;
using SoloCrm.Web.Components.Pages.Settings;

namespace SoloCrm.Web.Tests.Components.Pages;

public sealed class CalendarFeedSettingsSectionTests : BunitContext
{
    private const string Token = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFG";

    private readonly IQueryHandler<GetCalendarFeed.Query, GetCalendarFeed.Result> _get =
        Substitute.For<IQueryHandler<GetCalendarFeed.Query, GetCalendarFeed.Result>>();

    private readonly ICommandHandler<GenerateCalendarFeed.Command, GenerateCalendarFeed.Result> _generate =
        Substitute.For<ICommandHandler<GenerateCalendarFeed.Command, GenerateCalendarFeed.Result>>();

    public CalendarFeedSettingsSectionTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(new AppClock(TimeProvider.System, TimeZoneInfo.Utc));
        Services.AddSingleton(_get);
        Services.AddSingleton(_generate);
        Services.AddSingleton(Substitute.For<ICommandHandler<RevokeCalendarFeed.Command, RevokeCalendarFeed.Result>>());
        SetState(new GetCalendarFeed.Result(false, null, null));
    }

    [Fact]
    public void Render_NoFeed_OffersSetupAndHidesStatus()
    {
        var section = Render<CalendarFeedSettingsSection>();

        section.Markup.Should().Contain("Kein Feed eingerichtet").And.Contain("Feed einrichten");
        section.FindAll("[data-feed-status]").Should().BeEmpty();
    }

    [Fact]
    public void Render_ActiveFeed_ShowsStatusAndActions()
    {
        SetState(new GetCalendarFeed.Result(true, new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero), null));

        var section = Render<CalendarFeedSettingsSection>();

        section.Find("[data-feed-status]").TextContent.Should().Contain("zuletzt abgerufen: nie");
        section.Markup.Should().Contain("Neuen Link erzeugen").And.Contain("Deaktivieren").And.NotContain("Feed einrichten");
    }

    [Fact]
    public async Task Setup_NoFeed_ShowsFeedLinkOnceInDialog()
    {
        _generate.Handle(Arg.Any<GenerateCalendarFeed.Command>(), Arg.Any<CancellationToken>())
            .Returns(Result<GenerateCalendarFeed.Result>.Success(new GenerateCalendarFeed.Result(Token)));
        var dialogs = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        var section = Render<CalendarFeedSettingsSection>();
        SetState(new GetCalendarFeed.Result(true, DateTimeOffset.UtcNow, null));

        var click = section.InvokeAsync(() => section.Find("button").Click());

        dialogs.WaitForAssertion(() => dialogs.Find(".secret-value input").GetAttribute("value").Should().EndWith($"/calendar/{Token}.ics"));
        dialogs.FindAll("button").Single(b => b.TextContent.Contains("Fertig", StringComparison.Ordinal)).Click();
        await click;
        dialogs.WaitForAssertion(() => dialogs.Markup.Should().NotContain(Token));
        section.Markup.Should().NotContain(Token);
    }

    private void SetState(GetCalendarFeed.Result state) =>
        _get.Handle(Arg.Any<GetCalendarFeed.Query>(), Arg.Any<CancellationToken>()).Returns(Result<GetCalendarFeed.Result>.Success(state));
}
