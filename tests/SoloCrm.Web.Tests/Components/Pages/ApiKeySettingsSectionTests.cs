using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.ApiKeys;
using SoloCrm.Web.Components.Pages.Settings;

namespace SoloCrm.Web.Tests.Components.Pages;

public sealed class ApiKeySettingsSectionTests : BunitContext
{
    private const string Key = "scrm_abcdefgh_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static readonly DateTimeOffset Created = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly IQueryHandler<GetApiKeys.Query, GetApiKeys.Result> _get =
        Substitute.For<IQueryHandler<GetApiKeys.Query, GetApiKeys.Result>>();

    private readonly ICommandHandler<CreateApiKey.Command, CreateApiKey.Result> _create =
        Substitute.For<ICommandHandler<CreateApiKey.Command, CreateApiKey.Result>>();

    public ApiKeySettingsSectionTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(new AppClock(TimeProvider.System, TimeZoneInfo.Utc));
        Services.AddSingleton(_get);
        Services.AddSingleton(_create);
        Services.AddSingleton(Substitute.For<ICommandHandler<RevokeApiKey.Command, RevokeApiKey.Result>>());
        _get.Handle(Arg.Any<GetApiKeys.Query>(), Arg.Any<CancellationToken>())
            .Returns(Result<GetApiKeys.Result>.Success(new GetApiKeys.Result([])));
    }

    [Fact]
    public async Task Create_ValidName_ShowsKeyOnceThenOnlyThePrefix()
    {
        var id = Guid.CreateVersion7();
        _create.Handle(new CreateApiKey.Command("n8n"), Arg.Any<CancellationToken>())
            .Returns(Result<CreateApiKey.Result>.Success(new CreateApiKey.Result(id, Key, "abcdefgh")));
        var dialogs = Render<MudDialogProvider>();
        Render<MudPopoverProvider>();
        var section = Render<ApiKeySettingsSection>();
        section.Find("input").Input("n8n");
        _get.Handle(Arg.Any<GetApiKeys.Query>(), Arg.Any<CancellationToken>())
            .Returns(Result<GetApiKeys.Result>.Success(new GetApiKeys.Result(
                [new GetApiKeys.Item(id, "n8n", "abcdefgh", Created, null, null)])));

        var submit = section.InvokeAsync(() => section.Find("form").Submit());

        dialogs.WaitForAssertion(() => dialogs.Find(".secret-value input").GetAttribute("value").Should().Be(Key));
        dialogs.FindAll("button").Single(b => b.TextContent.Contains("Fertig", StringComparison.Ordinal)).Click();
        await submit;
        dialogs.WaitForAssertion(() => dialogs.Markup.Should().NotContain(Key));
        section.Markup.Should().Contain("scrm_abcdefgh_…").And.Contain("nie").And.NotContain(Key);
    }

    [Fact]
    public async Task Create_EmptyName_ShowsValidationMessageAtField()
    {
        _create.Handle(Arg.Any<CreateApiKey.Command>(), Arg.Any<CancellationToken>())
            .Returns(Result<CreateApiKey.Result>.Failure(new ValidationError(new Dictionary<string, string[]>
            {
                ["Name"] = ["Bitte einen Namen angeben, z. B. „n8n“."],
            })));
        var section = Render<ApiKeySettingsSection>();

        await section.Find("form").SubmitAsync();

        section.WaitForAssertion(() => section.Markup.Should().Contain("Bitte einen Namen angeben"));
    }

    [Fact]
    public void Render_RevokedKey_ShowsRevocationInsteadOfButton()
    {
        _get.Handle(Arg.Any<GetApiKeys.Query>(), Arg.Any<CancellationToken>())
            .Returns(Result<GetApiKeys.Result>.Success(new GetApiKeys.Result(
                [new GetApiKeys.Item(Guid.CreateVersion7(), "alt", "abcdefgh", Created, Created, Created.AddDays(1))])));

        var section = Render<ApiKeySettingsSection>();

        section.Markup.Should().Contain("widerrufen am 02.10.2026");
        section.FindAll("button").Should().NotContain(b => b.TextContent.Contains("Widerrufen", StringComparison.Ordinal));
    }
}
