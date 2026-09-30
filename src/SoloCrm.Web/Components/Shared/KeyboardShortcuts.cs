namespace SoloCrm.Web.Components.Shared;

/// <summary>
/// The keyboard shortcuts (SPEC 3.2): the targets reported by <c>keyboard.js</c> and the overview shown with <c>?</c>.
/// </summary>
public static class KeyboardShortcuts
{
    /// <summary>A shortcut for the overview; each entry of <see cref="Keys"/> is one key, pressed together or in sequence.</summary>
    public sealed record Shortcut(IReadOnlyList<string> Keys, string Description, bool InSequence = false);

    public sealed record Group(string Title, IReadOnlyList<Shortcut> Shortcuts);

    /// <summary>Navigation target of a <c>G</c> sequence reported as <c>"g h"</c>, <c>"g p"</c>, …</summary>
    public static string? NavigationTarget(string shortcut) => shortcut switch
    {
        "g h" => "/",
        "g p" => "/pipeline",
        "g k" => "/contacts",
        "g o" => "/organizations",
        _ => null,
    };

    public static IReadOnlyList<Group> Overview(bool isMac)
    {
        var modifier = isMac ? "⌘" : "Strg";
        return
        [
            new("Allgemein",
            [
                new([modifier, "K"], "Command Palette öffnen (auch in Eingabefeldern)"),
                new(["N"], "Quick-Add passend zur Seite (Kontakt, Organisation, Anfrage, Notiz)"),
                new(["?"], "Diese Übersicht"),
                new(["Esc"], "Dialog oder Palette schließen, Bearbeiten abbrechen"),
            ]),
            new("Navigation",
            [
                new(["G", "H"], "Gehe zu Heute", InSequence: true),
                new(["G", "P"], "Gehe zu Pipeline", InSequence: true),
                new(["G", "K"], "Gehe zu Kontakte", InSequence: true),
                new(["G", "O"], "Gehe zu Organisationen", InSequence: true),
            ]),
            new("Command Palette",
            [
                new(["↓"], "Nächster Eintrag"),
                new(["↑"], "Vorheriger Eintrag"),
                new(["Enter"], "Eintrag öffnen bzw. Aktion ausführen"),
            ]),
        ];
    }
}
