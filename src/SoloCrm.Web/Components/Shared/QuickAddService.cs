using MudBlazor;

namespace SoloCrm.Web.Components.Shared;

/// <summary>
/// Single entry point for quick-add (app bar button, page button, shortcut <c>N</c>).
/// Scoped per circuit; pages subscribe to <see cref="ContactCreated"/> to refresh their data.
/// </summary>
public sealed class QuickAddService(IDialogService dialogService, ISnackbar snackbar)
{
    private static readonly DialogOptions Options = new()
    {
        MaxWidth = MaxWidth.Small,
        FullWidth = true,
        CloseOnEscapeKey = true,
    };

    private bool _isOpen;

    public event Func<Guid, Task>? ContactCreated;

    public async Task OpenContactAsync()
    {
        // The shortcut may fire again while the dialog is already open.
        if (_isOpen)
        {
            return;
        }

        _isOpen = true;
        try
        {
            var dialog = await dialogService.ShowAsync<QuickAddContactDialog>("Neuer Kontakt", Options);
            var result = await dialog.Result;
            if (result is { Canceled: false, Data: Guid contactId })
            {
                snackbar.Add("Kontakt angelegt.", Severity.Success);
                await NotifyContactCreatedAsync(contactId);
            }
        }
        finally
        {
            _isOpen = false;
        }
    }

    private async Task NotifyContactCreatedAsync(Guid contactId)
    {
        if (ContactCreated is null)
        {
            return;
        }

        foreach (var handler in ContactCreated.GetInvocationList().Cast<Func<Guid, Task>>())
        {
            await handler(contactId);
        }
    }
}
