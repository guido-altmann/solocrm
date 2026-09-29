using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace SoloCrm.Web.Components.Shared;

public enum QuickAddTarget
{
    Contact,
    Organization,
    Opportunity,
}

/// <summary>
/// Single entry point for quick-add (app bar button, page buttons, shortcut <c>N</c>). The shortcut and the
/// app bar button create what fits the current page (SPEC 3.2). Scoped per circuit; pages subscribe to the
/// <c>…Created</c> events to refresh their data.
/// </summary>
public sealed class QuickAddService(IDialogService dialogService, ISnackbar snackbar, NavigationManager navigation)
{
    private static readonly DialogOptions Options = new()
    {
        MaxWidth = MaxWidth.Small,
        FullWidth = true,
        CloseOnEscapeKey = true,
    };

    private static readonly DialogOptions WideOptions = new()
    {
        MaxWidth = MaxWidth.Medium,
        FullWidth = true,
        CloseOnEscapeKey = true,
    };

    private bool _isOpen;

    public event Func<Guid, Task>? ContactCreated;

    public event Func<Guid, Task>? OrganizationCreated;

    public event Func<Guid, Task>? OpportunityCreated;

    public QuickAddTarget CurrentTarget
    {
        get
        {
            var path = navigation.ToBaseRelativePath(navigation.Uri);
            return path.StartsWith("organizations", StringComparison.OrdinalIgnoreCase) ? QuickAddTarget.Organization
                : path.StartsWith("pipeline", StringComparison.OrdinalIgnoreCase) ? QuickAddTarget.Opportunity
                : QuickAddTarget.Contact;
        }
    }

    public string CurrentLabel => CurrentTarget switch
    {
        QuickAddTarget.Organization => "Neue Organisation",
        QuickAddTarget.Opportunity => "Neue Anfrage",
        _ => "Neuer Kontakt",
    };

    public Task OpenForCurrentPageAsync() => CurrentTarget switch
    {
        QuickAddTarget.Organization => OpenOrganizationAsync(),
        QuickAddTarget.Opportunity => OpenOpportunityAsync(),
        _ => OpenContactAsync(),
    };

    public async Task OpenContactAsync()
    {
        if (await ShowAsync<ContactDialog>("Neuer Kontakt") is { } id)
        {
            snackbar.Add("Kontakt angelegt.", Severity.Success);
            await NotifyAsync(ContactCreated, id);
        }
    }

    public async Task OpenOrganizationAsync()
    {
        if (await ShowAsync<OrganizationDialog>("Neue Organisation") is { } id)
        {
            snackbar.Add("Organisation angelegt.", Severity.Success);
            await NotifyAsync(OrganizationCreated, id);
        }
    }

    public async Task OpenOpportunityAsync()
    {
        if (await ShowAsync<OpportunityDialog>("Neue Anfrage", WideOptions) is { } id)
        {
            snackbar.Add("Anfrage angelegt.", Severity.Success);
            await NotifyAsync(OpportunityCreated, id);
        }
    }

    /// <returns>The id of the created object, or <c>null</c> if the dialog was canceled.</returns>
    private async Task<Guid?> ShowAsync<TDialog>(string title, DialogOptions? options = null)
        where TDialog : IComponent
    {
        // The shortcut may fire again while a dialog is already open.
        if (_isOpen)
        {
            return null;
        }

        _isOpen = true;
        try
        {
            var dialog = await dialogService.ShowAsync<TDialog>(title, options ?? Options);
            var result = await dialog.Result;
            return result is { Canceled: false, Data: Guid id } ? id : null;
        }
        finally
        {
            _isOpen = false;
        }
    }

    private static async Task NotifyAsync(Func<Guid, Task>? handlers, Guid id)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Func<Guid, Task>>())
        {
            await handler(id);
        }
    }
}
