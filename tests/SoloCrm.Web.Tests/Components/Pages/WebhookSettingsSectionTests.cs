using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Webhooks;
using SoloCrm.Domain.Webhooks;
using SoloCrm.Web.Components.Pages.Settings;

namespace SoloCrm.Web.Tests.Components.Pages;

public sealed class WebhookSettingsSectionTests : BunitContext
{
    private const string Secret = "4f2c0a5e9b8d7c6f1e2d3c4b5a69788766554433221100ffeeddccbbaa998877";

    private static readonly string[] StageChanged = ["opportunity.stage_changed"];

    private static readonly DateTimeOffset Created = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly IQueryHandler<GetWebhooks.Query, GetWebhooks.Result> _get =
        Substitute.For<IQueryHandler<GetWebhooks.Query, GetWebhooks.Result>>();

    private readonly ICommandHandler<CreateWebhook.Command, CreateWebhook.Result> _create =
        Substitute.For<ICommandHandler<CreateWebhook.Command, CreateWebhook.Result>>();

    private readonly ICommandHandler<SendWebhookPing.Command, SendWebhookPing.Result> _ping =
        Substitute.For<ICommandHandler<SendWebhookPing.Command, SendWebhookPing.Result>>();

    public WebhookSettingsSectionTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(new AppClock(TimeProvider.System, TimeZoneInfo.Utc));
        Services.AddSingleton(_get);
        Services.AddSingleton(_create);
        Services.AddSingleton(_ping);
        Services.AddSingleton(Substitute.For<ICommandHandler<UpdateWebhook.Command, UpdateWebhook.Result>>());
        Services.AddSingleton(Substitute.For<IQueryHandler<GetWebhookDeliveries.Query, GetWebhookDeliveries.Result>>());
        Services.AddSingleton(Substitute.For<ICommandHandler<RegenerateWebhookSecret.Command, RegenerateWebhookSecret.Result>>());
        Services.AddSingleton(Substitute.For<ICommandHandler<DeleteWebhook.Command, DeleteWebhook.Result>>());
        _get.Handle(Arg.Any<GetWebhooks.Query>(), Arg.Any<CancellationToken>())
            .Returns(Result<GetWebhooks.Result>.Success(new GetWebhooks.Result([])));
    }

    [Fact]
    public async Task Create_ValidInput_ShowsSecretOnceAndHidesItAfterClosing()
    {
        var id = Guid.CreateVersion7();
        _create.Handle(Arg.Any<CreateWebhook.Command>(), Arg.Any<CancellationToken>())
            .Returns(Result<CreateWebhook.Result>.Success(new CreateWebhook.Result(id, Secret)));
        var (dialogs, section) = RenderSection();

        Button(section, "Neuer Webhook").Click();
        dialogs.WaitForAssertion(() => dialogs.FindAll("input").Should().NotBeEmpty());
        dialogs.FindAll("input")[0].Input("n8n");
        dialogs.FindAll("input")[1].Input("https://n8n.example.test/webhook/abc");
        dialogs.FindAll("input[type=checkbox]")[WebhookEvents.All.ToList().IndexOf("opportunity.stage_changed")].Change(true);
        _get.Handle(Arg.Any<GetWebhooks.Query>(), Arg.Any<CancellationToken>())
            .Returns(Result<GetWebhooks.Result>.Success(new GetWebhooks.Result(
                [new GetWebhooks.Item(id, "n8n", "https://n8n.example.test/webhook/abc", ["opportunity.stage_changed"], true, Created, null, null)])));
        await dialogs.Find("form").SubmitAsync();

        dialogs.WaitForAssertion(() => dialogs.Find(".secret-value input").GetAttribute("value").Should().Be(Secret));
        await _create.Received(1).Handle(
            Arg.Is<CreateWebhook.Command>(c => c.Name == "n8n" && c.Events!.SequenceEqual(StageChanged)),
            Arg.Any<CancellationToken>());

        Button(dialogs, "Fertig").Click();

        dialogs.WaitForAssertion(() => dialogs.Markup.Should().NotContain(Secret));
        section.WaitForAssertion(() => section.Markup.Should().Contain("Phase der Anfrage geändert"));
        section.Markup.Should().NotContain(Secret);
    }

    [Fact]
    public async Task Create_ValidationError_ShowsMessagesAtUrlAndEvents()
    {
        _create.Handle(Arg.Any<CreateWebhook.Command>(), Arg.Any<CancellationToken>())
            .Returns(Result<CreateWebhook.Result>.Failure(new ValidationError(new Dictionary<string, string[]>
            {
                ["Url"] = ["Bitte eine gültige https-URL angeben."],
                ["Events"] = ["Bitte mindestens ein Ereignis auswählen."],
            })));
        var (dialogs, section) = RenderSection();

        Button(section, "Neuer Webhook").Click();
        dialogs.WaitForAssertion(() => dialogs.FindAll("form").Should().ContainSingle());
        await dialogs.Find("form").SubmitAsync();

        dialogs.WaitForAssertion(() =>
        {
            dialogs.FindAll("label.mud-input-error").Select(l => l.TextContent.Trim()).Should().Equal("URL");
            dialogs.Find("#webhook-events-error").TextContent.Should().Contain("mindestens ein Ereignis");
        });
    }

    [Fact]
    public void Ping_FailingTarget_ShowsErrorInSnackbar()
    {
        var id = Guid.CreateVersion7();
        _get.Handle(Arg.Any<GetWebhooks.Query>(), Arg.Any<CancellationToken>())
            .Returns(Result<GetWebhooks.Result>.Success(new GetWebhooks.Result(
                [new GetWebhooks.Item(id, "n8n", "https://n8n.example.test", ["contact.created"], true, Created, null, null)])));
        _ping.Handle(new SendWebhookPing.Command(id), Arg.Any<CancellationToken>())
            .Returns(Result<SendWebhookPing.Result>.Success(new SendWebhookPing.Result(false, 500, 12, "HTTP 500")));
        var snackbars = Render<MudSnackbarProvider>();
        var (_, section) = RenderSection();

        section.Find("button[aria-label='Test an n8n senden']").Click();

        snackbars.WaitForAssertion(() => snackbars.Markup.Should().Contain("Test fehlgeschlagen: HTTP 500"));
    }

    private (IRenderedComponent<MudDialogProvider> Dialogs, IRenderedComponent<WebhookSettingsSection> Section) RenderSection()
    {
        var dialogs = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        return (dialogs, Render<WebhookSettingsSection>());
    }

    private static AngleSharp.Dom.IElement Button<T>(IRenderedComponent<T> fragment, string text)
        where T : Microsoft.AspNetCore.Components.IComponent =>
        fragment.FindAll("button").Single(b => b.TextContent.Contains(text, StringComparison.Ordinal));
}
