using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Web.Components.Shared;

namespace SoloCrm.Web.Tests.Components.Shared;

public sealed class LostReasonDialogTests : BunitContext
{
    private IRenderedComponent<MudPopoverProvider>? _popovers;

    public LostReasonDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    [Fact]
    public async Task Submit_WithoutReason_ShowsErrorAndStaysOpen()
    {
        var (provider, dialog) = await OpenDialogAsync();

        await provider.Find("form").SubmitAsync();

        provider.WaitForAssertion(() => provider.Markup.Should().Contain("Bitte einen Absagegrund wählen."));
        dialog.Result.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task Submit_WithReason_ClosesWithReason()
    {
        var (provider, dialog) = await OpenDialogAsync();

        await provider.Find(".mud-input-control.mud-select").MouseDownAsync(new());
        _popovers!.WaitForElements(".mud-list-item");
        _popovers!.FindAll(".mud-list-item").Single(i => i.TextContent.Contains("Preis", StringComparison.Ordinal)).Click();
        await provider.Find("form").SubmitAsync();

        var result = await dialog.Result;
        result!.Canceled.Should().BeFalse();
        result.Data.Should().Be(LostReason.Price);
    }

    [Fact]
    public async Task Cancel_Always_ReturnsCanceled()
    {
        var (provider, dialog) = await OpenDialogAsync();

        provider.FindAll("button").Single(b => b.TextContent.Contains("Abbrechen", StringComparison.Ordinal)).Click();

        (await dialog.Result)!.Canceled.Should().BeTrue();
    }

    private async Task<(IRenderedComponent<MudDialogProvider> Provider, IDialogReference Dialog)> OpenDialogAsync()
    {
        var provider = Render<MudDialogProvider>();
        _popovers = Render<MudPopoverProvider>();
        var parameters = new DialogParameters<LostReasonDialog> { { d => d.Title, "Migration Azure" } };
        var dialog = await provider.InvokeAsync(() =>
            Services.GetRequiredService<IDialogService>().ShowAsync<LostReasonDialog>("Anfrage verloren", parameters));
        return (provider, dialog);
    }
}
