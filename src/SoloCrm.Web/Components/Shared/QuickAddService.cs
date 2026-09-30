using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace SoloCrm.Web.Components.Shared;

public enum QuickAddTarget
{
    Contact,
    Organization,
    Opportunity,
    Note,
}

/// <summary>
/// Single entry point for quick-add (app bar button, page buttons, shortcut <c>N</c>). The shortcut and the
/// app bar button create what fits the current page (SPEC 3.2). Scoped per circuit; pages subscribe to the
/// <c>…Created</c> events to refresh their data. On a detail view, quick-add focuses the note input of its timeline.
/// </summary>
public sealed class QuickAddService(IDialogService dialogService, ISnackbar snackbar, NavigationManager navigation)
{
    private bool _isOpen;
    private NoteInput? _noteInput;

    public event Func<Guid, Task>? ContactCreated;

    public event Func<Guid, Task>? OrganizationCreated;

    public event Func<Guid, Task>? OpportunityCreated;

    public event Func<Guid, Task>? TaskCreated;

    /// <summary>Raised when <see cref="CurrentTarget"/> may have changed without navigation.</summary>
    public event Action? TargetChanged;

    public QuickAddTarget CurrentTarget
    {
        get
        {
            var path = navigation.ToBaseRelativePath(navigation.Uri);
            if (_noteInput is not null && IsDetailPath(path))
            {
                return QuickAddTarget.Note;
            }

            return path.StartsWith("organizations", StringComparison.OrdinalIgnoreCase) ? QuickAddTarget.Organization
                : path.StartsWith("pipeline", StringComparison.OrdinalIgnoreCase) ? QuickAddTarget.Opportunity
                : QuickAddTarget.Contact;
        }
    }

    public string CurrentLabel => CurrentTarget switch
    {
        QuickAddTarget.Organization => "Neue Organisation",
        QuickAddTarget.Opportunity => "Neue Anfrage",
        QuickAddTarget.Note => "Neue Notiz",
        _ => "Neuer Kontakt",
    };

    public Task OpenForCurrentPageAsync() => CurrentTarget switch
    {
        QuickAddTarget.Organization => OpenOrganizationAsync(),
        QuickAddTarget.Opportunity => OpenOpportunityAsync(),
        QuickAddTarget.Note => _noteInput!.Focus(),
        _ => OpenContactAsync(),
    };

    /// <summary>
    /// Registers the note input of a detail view for quick-add; dispose the result when the view goes away.
    /// A later registration (e.g. the next detail view) replaces an earlier one.
    /// </summary>
    public IDisposable RegisterNoteInput(Func<Task> focus)
    {
        var input = new NoteInput(this, focus);
        _noteInput = input;
        TargetChanged?.Invoke();
        return input;
    }

    private static bool IsDetailPath(string path)
    {
        var segments = path.Split('?', '#')[0].Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments is ["contacts" or "organizations" or "opportunities", var id] && Guid.TryParse(id, out _);
    }

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
        if (await ShowAsync<OpportunityDialog>("Neue Anfrage", DialogDefaults.Medium) is { } id)
        {
            snackbar.Add("Anfrage angelegt.", Severity.Success);
            await NotifyAsync(OpportunityCreated, id);
        }
    }

    /// <summary>Free task; the dialog offers to link it to a contact, organization or request (US-11 AK1).</summary>
    public async Task OpenTaskAsync()
    {
        if (await ShowAsync<TaskDialog>("Neue Aufgabe") is { } id)
        {
            snackbar.Add("Aufgabe angelegt.", Severity.Success);
            await NotifyAsync(TaskCreated, id);
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
            var dialog = await dialogService.ShowAsync<TDialog>(title, options ?? DialogDefaults.Small);
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

    private sealed class NoteInput(QuickAddService owner, Func<Task> focus) : IDisposable
    {
        public Func<Task> Focus { get; } = focus;

        public void Dispose()
        {
            if (owner._noteInput == this)
            {
                owner._noteInput = null;
                owner.TargetChanged?.Invoke();
            }
        }
    }
}
