using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SoloCrm.Application.Abstractions;
using SoloCrm.Web.Components.Shared;
using SearchFeature = SoloCrm.Application.Features.Search.Search;

namespace SoloCrm.Web.Tests.Components.Shared;

public sealed class CommandPaletteServiceTests : BunitContext
{
    private readonly CommandPaletteService _palette;
    private readonly NavigationManager _navigation;

    public CommandPaletteServiceTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(Substitute.For<IQueryHandler<SearchFeature.Query, SearchFeature.Result>>());
        _navigation = Services.GetRequiredService<NavigationManager>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        var quickAdd = new QuickAddService(dialogs, Services.GetRequiredService<ISnackbar>(), _navigation);
        _palette = new CommandPaletteService(dialogs, quickAdd, _navigation);
    }

    [Fact]
    public async Task RunAsync_RecordLink_NavigatesToDetailView()
    {
        var id = Guid.CreateVersion7();

        await _palette.RunAsync(new PaletteItem(PaletteGroup.Contacts, "Ada Lovelace", null, "", Links.Contact(id)));

        _navigation.Uri.Should().EndWith($"/contacts/{id}");
    }

    [Fact]
    public async Task OpenAsync_EntryChosen_RunsItAfterClosing()
    {
        var provider = Render<MudDialogProvider>();
        var open = provider.InvokeAsync(_palette.OpenAsync);
        var input = provider.WaitForElement("input[role=combobox]");

        await input.InputAsync(new ChangeEventArgs { Value = "pipe" });
        await provider.Find("input[role=combobox]").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        await open;

        _navigation.Uri.Should().EndWith("/pipeline");
        _palette.IsOpen.Should().BeFalse();
        provider.FindAll(".command-palette-list").Should().BeEmpty();
    }

    [Fact]
    public async Task OpenAsync_AlreadyOpen_IgnoresSecondCall()
    {
        var provider = Render<MudDialogProvider>();
        _ = provider.InvokeAsync(_palette.OpenAsync);
        provider.WaitForElement("input[role=combobox]");

        await provider.InvokeAsync(_palette.OpenAsync);

        _palette.IsOpen.Should().BeTrue();
        provider.FindAll("input[role=combobox]").Should().HaveCount(1);
    }
}
