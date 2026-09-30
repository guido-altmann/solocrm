using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace SoloCrm.Web.Components.Shared;

/// <summary>
/// Opens the command palette (<c>Ctrl/Cmd + K</c>, search field in the app bar) and runs the chosen entry once the
/// palette has closed, so quick-add dialogs never open on top of it (US-13). Scoped per circuit.
/// </summary>
public sealed class CommandPaletteService(IDialogService dialogService, QuickAddService quickAdd, NavigationManager navigation)
{
    private static readonly DialogOptions Options = new()
    {
        Position = DialogPosition.TopCenter,
        MaxWidth = MaxWidth.Small,
        FullWidth = true,
        NoHeader = true,
        CloseOnEscapeKey = true,
    };

    public bool IsOpen { get; private set; }

    public async Task OpenAsync()
    {
        // The shortcut may fire again while the palette is open.
        if (IsOpen)
        {
            return;
        }

        PaletteItem? item;
        IsOpen = true;
        try
        {
            var dialog = await dialogService.ShowAsync<CommandPalette>("Suchen", Options);
            item = await dialog.Result is { Canceled: false, Data: PaletteItem chosen } ? chosen : null;
        }
        finally
        {
            IsOpen = false;
        }

        if (item is not null)
        {
            await RunAsync(item);
        }
    }

    public Task RunAsync(PaletteItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.Href is { } href)
        {
            navigation.NavigateTo(href);
            return Task.CompletedTask;
        }

        return item.Action switch
        {
            PaletteAction.NewContact => quickAdd.OpenContactAsync(),
            PaletteAction.NewOrganization => quickAdd.OpenOrganizationAsync(),
            PaletteAction.NewOpportunity => quickAdd.OpenOpportunityAsync(),
            PaletteAction.NewTask => quickAdd.OpenTaskAsync(),
            _ => Task.CompletedTask,
        };
    }
}
