using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MudBlazor.Services;
using NSubstitute;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Nextcloud;
using SoloCrm.Web.Components.Pages.Settings;

namespace SoloCrm.Web.Tests.Components.Pages;

public sealed class NextcloudSettingsSectionTests : BunitContext
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 10, 0, 0, TimeSpan.Zero);

    private readonly IQueryHandler<GetNextcloudStatus.Query, GetNextcloudStatus.Result> _status =
        Substitute.For<IQueryHandler<GetNextcloudStatus.Query, GetNextcloudStatus.Result>>();

    private readonly ICommandHandler<ConnectNextcloud.Command, ConnectNextcloud.Result> _connect =
        Substitute.For<ICommandHandler<ConnectNextcloud.Command, ConnectNextcloud.Result>>();

    private readonly ICommandHandler<SyncNextcloud.Command, SyncNextcloud.Result> _sync =
        Substitute.For<ICommandHandler<SyncNextcloud.Command, SyncNextcloud.Result>>();

    public NextcloudSettingsSectionTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(_status);
        Services.AddSingleton(_connect);
        Services.AddSingleton(_sync);
        Services.AddSingleton(Substitute.For<ICommandHandler<DisconnectNextcloud.Command, DisconnectNextcloud.Result>>());
        Services.AddSingleton(new AppClock(new FakeTimeProvider(Now), TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")));
        _sync.Handle(Arg.Any<SyncNextcloud.Command>(), Arg.Any<CancellationToken>())
            .Returns(Result<SyncNextcloud.Result>.Success(new SyncNextcloud.Result(2, 0, 0)));
    }

    private static GetNextcloudStatus.Connection Connection(string? error = null, bool rejected = false) =>
        new("https://cloud.example.test", "guido", Now.AddDays(-1), Now.AddMinutes(-5), error, rejected, 42);

    private void Status(GetNextcloudStatus.Connection? connection) =>
        _status.Handle(Arg.Any<GetNextcloudStatus.Query>(), Arg.Any<CancellationToken>())
            .Returns(Result<GetNextcloudStatus.Result>.Success(new GetNextcloudStatus.Result(connection)));

    private static void Fill(IRenderedComponent<NextcloudSettingsSection> section, string server, string user, string password)
    {
        var inputs = section.FindAll("[data-testid=nextcloud-form] input");
        inputs[0].Input(server);
        inputs[1].Input(user);
        inputs[2].Input(password);
    }

    [Fact]
    public void Render_NotConnected_ShowsFormWithAppPasswordHint()
    {
        Status(null);

        var section = Render<NextcloudSettingsSection>();

        section.Markup.Should().Contain("App-Passwort").And.Contain("automatisch angelegt");
        section.Find("[data-testid=nextcloud-connect]").TextContent.Should().Contain("Verbinden");
        section.FindAll("[data-testid=nextcloud-form] input").Should().HaveCount(3);
        section.FindAll("[data-testid=nextcloud-sync]").Should().BeEmpty();
    }

    [Fact]
    public async Task Submit_Valid_ConnectsAndRunsTheFirstSync()
    {
        Status(null);
        _connect.Handle(Arg.Any<ConnectNextcloud.Command>(), Arg.Any<CancellationToken>())
            .Returns(Result<ConnectNextcloud.Result>.Success(new ConnectNextcloud.Result("https://cloud.example.test", "guido")));
        var section = Render<NextcloudSettingsSection>();
        Fill(section, "https://cloud.example.test", "guido", "app-password");

        await section.Find("[data-testid=nextcloud-form]").SubmitAsync();

        await _connect.Received(1).Handle(new ConnectNextcloud.Command("https://cloud.example.test", "guido", "app-password"), Arg.Any<CancellationToken>());
        await _sync.Received(1).Handle(new SyncNextcloud.Command(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Submit_Invalid_ShowsTheMessagesAtTheFields()
    {
        Status(null);
        _connect.Handle(Arg.Any<ConnectNextcloud.Command>(), Arg.Any<CancellationToken>())
            .Returns(Result<ConnectNextcloud.Result>.Failure(new ValidationError(new Dictionary<string, string[]>
            {
                [nameof(ConnectNextcloud.Command.ServerUrl)] = ["Bitte eine vollständige https-Adresse angeben."],
            })));
        var section = Render<NextcloudSettingsSection>();
        Fill(section, "http://cloud.example.test", "guido", "x");

        await section.Find("[data-testid=nextcloud-form]").SubmitAsync();

        section.Markup.Should().Contain("Bitte eine vollständige https-Adresse angeben.");
        await _sync.DidNotReceive().Handle(Arg.Any<SyncNextcloud.Command>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Submit_ServerRejects_ShowsTheReason()
    {
        Status(null);
        _connect.Handle(Arg.Any<ConnectNextcloud.Command>(), Arg.Any<CancellationToken>())
            .Returns(Result<ConnectNextcloud.Result>.Failure(Error.Failure("Nextcloud.ConnectFailed", "Nextcloud hat die Anmeldung abgelehnt.")));
        var section = Render<NextcloudSettingsSection>();
        Fill(section, "https://cloud.example.test", "guido", "falsch");

        await section.Find("[data-testid=nextcloud-form]").SubmitAsync();

        section.Find("[data-testid=nextcloud-connect-error]").TextContent.Should().Be("Nextcloud hat die Anmeldung abgelehnt.");
    }

    [Fact]
    public void Render_Connected_ShowsStatusLocalTimesAndActions()
    {
        Status(Connection(error: "Nextcloud ist gerade nicht erreichbar."));

        var section = Render<NextcloudSettingsSection>();

        section.Find("[data-testid=nextcloud-server]").TextContent.Should().Be("https://cloud.example.test");
        section.Find("[data-testid=nextcloud-last-sync]").TextContent.Trim().Should().Be("16.10.2026, 11:55");
        section.Find("[data-testid=nextcloud-contacts]").TextContent.Should().Be("42");
        section.Find("[data-testid=nextcloud-error]").TextContent.Should().Be("Nextcloud ist gerade nicht erreichbar.");
        section.FindAll("[data-testid=nextcloud-sync]").Should().HaveCount(1);
        section.FindAll("[data-testid=nextcloud-form]").Should().BeEmpty();
    }

    [Fact]
    public void Render_CredentialRejected_ShowsPausedHintAndTheFormInsteadOfSync()
    {
        Status(Connection(error: "Nextcloud hat die Anmeldung abgelehnt.", rejected: true));

        var section = Render<NextcloudSettingsSection>();

        section.Find("[data-testid=nextcloud-paused]").TextContent.Should().Contain("pausiert").And.Contain("Anmeldung abgelehnt");
        section.Find("[data-testid=nextcloud-connect]").TextContent.Should().Contain("Zugangsdaten speichern");
        section.FindAll("[data-testid=nextcloud-sync]").Should().BeEmpty();
        section.FindAll("[data-testid=nextcloud-disconnect]").Should().HaveCount(1);
    }

    [Fact]
    public void Click_ChangeCredentials_ShowsFormPrefilledWithoutPassword()
    {
        Status(Connection());
        var section = Render<NextcloudSettingsSection>();

        section.Find("[data-testid=nextcloud-edit]").Click();

        var inputs = section.FindAll("[data-testid=nextcloud-form] input");
        inputs[0].GetAttribute("value").Should().Be("https://cloud.example.test");
        inputs[1].GetAttribute("value").Should().Be("guido");
        inputs[2].GetAttribute("value").Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Click_SyncNow_RunsTheSyncAndReloadsTheStatus()
    {
        Status(Connection());
        var section = Render<NextcloudSettingsSection>();

        await section.Find("[data-testid=nextcloud-sync]").ClickAsync(new());

        await _sync.Received(1).Handle(new SyncNextcloud.Command(), Arg.Any<CancellationToken>());
        await _status.Received(2).Handle(Arg.Any<GetNextcloudStatus.Query>(), Arg.Any<CancellationToken>());
    }
}
