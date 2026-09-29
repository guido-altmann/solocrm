namespace SoloCrm.Application.Abstractions;

/// <summary>
/// Builds patterns for <c>EF.Functions.ILike</c> with <see cref="Escape"/> as escape character,
/// so user input containing <c>%</c> or <c>_</c> is matched literally.
/// </summary>
public static class LikePattern
{
    public const string Escape = "\\";

    public static string Contains(string value) => $"%{EscapeValue(value.Trim())}%";

    public static string StartsWith(string value) => $"{EscapeValue(value.Trim())}%";

    private static string EscapeValue(string value) => value
        .Replace(Escape, Escape + Escape, StringComparison.Ordinal)
        .Replace("%", Escape + "%", StringComparison.Ordinal)
        .Replace("_", Escape + "_", StringComparison.Ordinal);
}
