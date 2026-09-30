using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.Tags;

/// <summary>
/// A label for contacts, organizations and requests across their types (SPEC 2.3, US-15). The name is unique
/// regardless of case; the database enforces this with a <c>citext</c> column.
/// </summary>
public sealed class Tag : Entity, IAuditable
{
    public const int NameMaxLength = 50;

    // Required by EF Core.
    private Tag()
    {
        Name = null!;
        Color = null!;
    }

    private Tag(string name, string color)
    {
        Name = name;
        Color = color;
    }

    public string Name { get; private set; }

    /// <summary>Hex color from <see cref="TagPalette"/>, e.g. <c>#1D4ED8</c>.</summary>
    public string Color { get; private set; }

    public static Tag Create(string name, string color) => new(NormalizeName(name), RequireColor(color));

    public void Rename(string name) => Name = NormalizeName(name);

    public void ChangeColor(string color) => Color = RequireColor(color);

    /// <summary>Trims the name and collapses inner whitespace („ Kunde   A “ → „Kunde A“).</summary>
    public static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A tag requires a name.", nameof(name));
        }

        var normalized = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= NameMaxLength
            ? normalized
            : throw new ArgumentException($"A tag name has at most {NameMaxLength} characters.", nameof(name));
    }

    private static string RequireColor(string color) =>
        TagPalette.Contains(color)
            ? TagPalette.Normalize(color)
            : throw new ArgumentException($"'{color}' is not a palette color.", nameof(color));
}
