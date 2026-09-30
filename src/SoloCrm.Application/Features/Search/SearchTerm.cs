using System.Text.RegularExpressions;

namespace SoloCrm.Application.Features.Search;

/// <summary>
/// Normalized user input for the search (ADR-007): lower-case words without punctuation. <see cref="TsQuery"/> matches
/// every word as prefix (<c>schmi:* &amp; max:*</c>); <see cref="Text"/> is compared by trigram word similarity.
/// Only letters and digits reach <c>to_tsquery</c>, so no input can break its syntax.
/// </summary>
public sealed partial record SearchTerm
{
    /// <summary>Longer input is cut off; names and titles are at most 200 characters anyway.</summary>
    public const int MaxLength = 200;

    private SearchTerm(IReadOnlyList<string> words)
    {
        Text = string.Join(' ', words);
        TsQuery = string.Join(" & ", words.Select(w => $"{w}:*"));
    }

    /// <summary>Words joined by single spaces, e.g. „max schmitt“.</summary>
    public string Text { get; }

    /// <summary>Prefix query for <c>to_tsquery('simple', …)</c>.</summary>
    public string TsQuery { get; }

    /// <returns><c>null</c> if the input contains no letters or digits.</returns>
    public static SearchTerm? Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var text = input.Length > MaxLength ? input[..MaxLength] : input;
        var words = Words().Matches(text).Select(m => m.Value.ToLowerInvariant()).ToList();
        return words.Count == 0 ? null : new SearchTerm(words);
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Words();
}
