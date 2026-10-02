using System.Globalization;
using System.Text;

namespace SoloCrm.Web.Components.Shared;

/// <summary>Names of files offered for download.</summary>
public static class FileNames
{
    /// <summary>GDPR export (US-19): „kontakt-ada-lovelace-2026-10-02.json“.</summary>
    public static string ContactExport(string name, DateOnly date) =>
        $"kontakt-{Slug(name)}-{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.json";

    /// <summary>Lower case ASCII letters and digits separated by single hyphens; German umlauts are transcribed.</summary>
    public static string Slug(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var transcribed = value.ToLowerInvariant()
            .Replace("ä", "ae", StringComparison.Ordinal)
            .Replace("ö", "oe", StringComparison.Ordinal)
            .Replace("ü", "ue", StringComparison.Ordinal)
            .Replace("ß", "ss", StringComparison.Ordinal)
            .Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder(transcribed.Length);
        foreach (var c in transcribed)
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(c);
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().TrimEnd('-');
        return slug.Length == 0 ? "ohne-namen" : slug;
    }
}
