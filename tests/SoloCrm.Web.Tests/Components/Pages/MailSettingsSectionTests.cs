using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MudBlazor.Services;
using NSubstitute;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Mail;
using SoloCrm.Web.Components.Pages.Settings;

namespace SoloCrm.Web.Tests.Components.Pages;

public sealed class MailSettingsSectionTests : BunitContext
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);

    private readonly IQueryHandler<GetMailStatus.Query, GetMailStatus.Result> _status =
        Substitute.For<IQueryHandler<GetMailStatus.Query, GetMailStatus.Result>>();

    private readonly ICommandHandler<SyncMail.Command, SyncMail.Result> _sync =
        Substitute.For<ICommandHandler<SyncMail.Command, SyncMail.Result>>();

    public MailSettingsSectionTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(_status);
        Services.AddSingleton(_sync);
        Services.AddSingleton(Substitute.For<ICommandHandler<DisconnectMail.Command, DisconnectMail.Result>>());
        Services.AddSingleton(new AppClock(new FakeTimeProvider(Now), TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")));
    }

    private void Status(bool configured, MailConnectionInfo? connection = null, int backfillDays = 30) =>
        _status.Handle(Arg.Any<GetMailStatus.Query>(), Arg.Any<CancellationToken>())
            .Returns(Result<GetMailStatus.Result>.Success(new GetMailStatus.Result(configured, connection, backfillDays)));

    [Fact]
    public void Render_NotConfigured_ExplainsTheSetupAndOffersNoConnect()
    {
        Status(configured: false);

        var section = Render<MailSettingsSection>();

        section.Find("[data-testid=mail-not-configured]").TextContent.Should().Contain("Mail__Graph__ClientId").And.Contain("email-logging-einrichtung");
        section.FindAll("[data-testid=mail-connect]").Should().BeEmpty();
    }

    [Fact]
    public void Render_ConfiguredWithoutConnection_ShowsBackfillAndConnectButton()
    {
        Status(configured: true, backfillDays: 14);

        var section = Render<MailSettingsSection>();

        section.Markup.Should().Contain("Kein Postfach verbunden").And.Contain("die letzten 14 Tage");
        section.FindAll("[data-testid=mail-sync]").Should().BeEmpty();
    }

    [Fact]
    public void Render_BackfillZero_SaysOnlyNewMails()
    {
        Status(configured: true, backfillDays: 0);

        var section = Render<MailSettingsSection>();

        section.Markup.Should().Contain("nur neue E-Mails ab dem Verbinden");
    }

    [Fact]
    public void Click_Connect_LoadsTheConnectEndpointWithAFullPageLoad()
    {
        Status(configured: true);
        var navigation = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
        var section = Render<MailSettingsSection>();

        section.Find("[data-testid=mail-connect]").Click();

        navigation.History.Should().ContainSingle()
            .Which.Should().Match<NavigationHistory>(h => h.Uri == "mail/connect" && h.Options.ForceLoad);
    }

    [Fact]
    public void Render_Connected_ShowsAccountLocalTimesAndError()
    {
        Status(
            configured: true,
            new MailConnectionInfo("Graph", "guido@example.test", Now.AddDays(-2), Now.AddMinutes(-5), "Die Anmeldung ist abgelaufen."));

        var section = Render<MailSettingsSection>();

        section.Find("[data-testid=mail-account]").TextContent.Should().Be("guido@example.test");
        section.Find("[data-testid=mail-last-sync]").TextContent.Trim().Should().Be("14.10.2026, 11:55");
        section.Find("[data-testid=mail-error]").TextContent.Should().Be("Die Anmeldung ist abgelaufen.");
        section.FindAll("[data-testid=mail-sync]").Should().HaveCount(1);
        section.FindAll("[data-testid=mail-disconnect]").Should().HaveCount(1);
    }

    [Fact]
    public void Render_ConnectedButNeverSynced_SaysNotYet()
    {
        Status(configured: true, new MailConnectionInfo("Graph", "guido@example.test", Now, null, null));

        var section = Render<MailSettingsSection>();

        section.Find("[data-testid=mail-last-sync]").TextContent.Trim().Should().Be("noch nicht");
        section.FindAll("[data-testid=mail-error]").Should().BeEmpty();
    }

    [Fact]
    public async Task Click_SyncNow_RunsTheSyncAndReloadsTheStatus()
    {
        Status(configured: true, new MailConnectionInfo("Graph", "guido@example.test", Now, null, null));
        _sync.Handle(Arg.Any<SyncMail.Command>(), Arg.Any<CancellationToken>())
            .Returns(Result<SyncMail.Result>.Success(new SyncMail.Result(3, 0, 1)));
        var section = Render<MailSettingsSection>();

        await section.Find("[data-testid=mail-sync]").ClickAsync(new());

        await _sync.Received(1).Handle(new SyncMail.Command(), Arg.Any<CancellationToken>());
        await _status.Received(2).Handle(Arg.Any<GetMailStatus.Query>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("connected", "Das Postfach ist verbunden.")]
    [InlineData("denied", "abgebrochen oder abgelehnt")]
    [InlineData("expired", "Die Anmeldung ist abgelaufen")]
    [InlineData("failed", "Das Verbinden ist fehlgeschlagen")]
    [InlineData("notconfigured", "nicht eingerichtet")]
    public void Render_Notice_ShowsTheOutcome(string notice, string text)
    {
        Status(configured: true);

        var section = Render<MailSettingsSection>(p => p.Add(s => s.Notice, notice));

        section.Find("[data-testid=mail-notice]").TextContent.Should().Contain(text);
    }

    [Fact]
    public void Render_UnknownNotice_ShowsNothing()
    {
        Status(configured: true);

        var section = Render<MailSettingsSection>(p => p.Add(s => s.Notice, "<script>alert(1)</script>"));

        section.FindAll("[data-testid=mail-notice]").Should().BeEmpty();
        section.Markup.Should().NotContain("<script>alert");
    }
}
