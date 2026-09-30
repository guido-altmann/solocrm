namespace SoloCrm.Domain.Tags;

/// <summary>
/// Fixed tag colors (SPEC 2.3, iteration 4 decision 7). Tags are shown as filled chips with white text; every color has
/// a contrast ratio of at least 4.5:1 to white (WCAG AA), so the chips stay readable in light and dark mode.
/// </summary>
public static class TagPalette
{
    public static IReadOnlyList<string> Colors { get; } =
    [
        "#1D4ED8", // blue
        "#15803D", // green
        "#B91C1C", // red
        "#7E22CE", // purple
        "#C2410C", // orange
        "#0F766E", // teal
        "#BE185D", // pink
        "#4D7C0F", // lime
        "#4338CA", // indigo
        "#A16207", // amber
        "#0E7490", // cyan
        "#475569", // slate
    ];

    public static bool Contains(string? color) =>
        color is not null && Colors.Contains(color, StringComparer.OrdinalIgnoreCase);

    /// <summary>Canonical spelling of a palette color (upper case, as in <see cref="Colors"/>).</summary>
    public static string Normalize(string color) =>
        Colors.FirstOrDefault(c => string.Equals(c, color, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"'{color}' is not a palette color.", nameof(color));

    /// <summary>New tags get the colors in turn: the one after the color of the most recently created tag.</summary>
    public static string Next(string? previousColor)
    {
        var index = previousColor is null
            ? -1
            : Colors.ToList().FindIndex(c => string.Equals(c, previousColor, StringComparison.OrdinalIgnoreCase));
        return Colors[(index + 1) % Colors.Count];
    }
}
