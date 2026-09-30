using MudBlazor;

namespace SoloCrm.Web.Components.Shared;

public enum PaletteGroup
{
    Actions,
    Contacts,
    Organizations,
    Opportunities,
}

public enum PaletteAction
{
    NewContact,
    NewOrganization,
    NewOpportunity,
    NewTask,
}

/// <summary>
/// Entry of the command palette (SPEC 3.1 Nr. 2): either a page or record to open (<see cref="Href"/>) or a
/// quick-add <see cref="Action"/>.
/// </summary>
public sealed record PaletteItem(
    PaletteGroup Group,
    string Label,
    string? Detail,
    string Icon,
    string? Href = null,
    PaletteAction? Action = null)
{
    /// <summary>Shown without search text; with search text only those whose label contains every word.</summary>
    public static IReadOnlyList<PaletteItem> Actions { get; } =
    [
        new(PaletteGroup.Actions, "Neuer Kontakt", null, Icons.Material.Filled.PersonAdd, Action: PaletteAction.NewContact),
        new(PaletteGroup.Actions, "Neue Organisation", null, Icons.Material.Filled.AddBusiness, Action: PaletteAction.NewOrganization),
        new(PaletteGroup.Actions, "Neue Anfrage", null, Icons.Material.Filled.PostAdd, Action: PaletteAction.NewOpportunity),
        new(PaletteGroup.Actions, "Neue Aufgabe", null, Icons.Material.Filled.AddTask, Action: PaletteAction.NewTask),
        new(PaletteGroup.Actions, "Gehe zu Heute", "G H", Icons.Material.Filled.Today, Href: "/"),
        new(PaletteGroup.Actions, "Gehe zu Pipeline", "G P", Icons.Material.Filled.ViewKanban, Href: "/pipeline"),
        new(PaletteGroup.Actions, "Gehe zu Kontakte", "G K", Icons.Material.Filled.Person, Href: "/contacts"),
        new(PaletteGroup.Actions, "Gehe zu Organisationen", "G O", Icons.Material.Filled.Business, Href: "/organizations"),
        new(PaletteGroup.Actions, "Gehe zu Einstellungen", null, Icons.Material.Filled.Settings, Href: "/settings"),
    ];

    public static IEnumerable<PaletteItem> ActionsMatching(string? text)
    {
        var words = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Actions.Where(a => words.All(w => a.Label.Contains(w, StringComparison.CurrentCultureIgnoreCase)));
    }
}
