using System.Text.Json;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Enrichment;
using SoloCrm.Domain.Enrichment;

namespace SoloCrm.Infrastructure.Enrichment;

/// <summary>An answer value before the citation check.</summary>
internal sealed record RawSuggestion(EnrichmentField Field, string Value, IReadOnlyList<string> SourceUrls, decimal Confidence);

/// <summary>The valid values of an answer and how many were unusable (blank value, no source, bad address).</summary>
internal sealed record ParsedAnswer(IReadOnlyList<RawSuggestion> Suggestions, int Discarded);

/// <summary>
/// Validates the answer of the agent strictly against <see cref="EnrichmentSchema"/> (ADR-016, point 5). A violation of
/// the structure fails the whole answer; a value that is merely unusable is discarded on its own. Error messages name
/// schema properties only, never values or URLs.
/// </summary>
internal static class EnrichmentResponseParser
{
    public static ParsedAnswer Parse(string text)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(StripCodeFence(text));
        }
        catch (JsonException)
        {
            throw Invalid("keine gültige JSON-Antwort");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("kein JSON-Objekt");
            }

            RequireExactProperties(root, EnrichmentSchema.Fields.Keys, "Antwort");

            var suggestions = new List<RawSuggestion>();
            var discarded = 0;
            foreach (var (name, field) in EnrichmentSchema.Fields)
            {
                var element = root.GetProperty(name);
                if (element.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                var suggestion = ParseField(name, field, element);
                if (suggestion is null)
                {
                    discarded++;
                }
                else
                {
                    suggestions.Add(suggestion);
                }
            }

            return new ParsedAnswer(suggestions, discarded);
        }
    }

    private static RawSuggestion? ParseField(string name, EnrichmentField field, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"'{name}' ist weder null noch ein Objekt");
        }

        RequireExactProperties(element, [EnrichmentSchema.ValueKey, EnrichmentSchema.SourceUrlsKey, EnrichmentSchema.ConfidenceKey], name);

        var confidence = ParseConfidence(name, element.GetProperty(EnrichmentSchema.ConfidenceKey));
        var urls = ParseUrls(name, element.GetProperty(EnrichmentSchema.SourceUrlsKey));
        var value = field == EnrichmentField.Address
            ? ParseAddress(name, element.GetProperty(EnrichmentSchema.ValueKey))
            : ParseText(name, element.GetProperty(EnrichmentSchema.ValueKey));

        // Unusable, but not a schema violation: no value, no source.
        return value is null || urls.Count == 0 ? null : new RawSuggestion(field, value, urls, confidence);
    }

    private static string? ParseText(string name, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw Invalid($"'{name}.value' ist kein Text");
        }

        var text = value.GetString()!.Trim();
        return text.Length is 0 or > EnrichmentSuggestion.ValueMaxLength ? null : text;
    }

    private static string? ParseAddress(string name, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"'{name}.value' ist kein Objekt");
        }

        RequireExactProperties(value, EnrichmentSchema.AddressParts, $"{name}.value");
        var parts = EnrichmentSchema.AddressParts.Select(part => OptionalString(value.GetProperty(part), $"{name}.value.{part}")).ToArray();

        try
        {
            return EnrichmentAddress.From(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5]).ToJson();
        }
        catch (ArgumentException)
        {
            // Empty, too long, or an unknown country code.
            return null;
        }
    }

    private static string? OptionalString(JsonElement element, string path) => element.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => element.GetString(),
        _ => throw Invalid($"'{path}' ist kein Text"),
    };

    private static decimal ParseConfidence(string name, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDecimal(out var confidence) || confidence is < 0m or > 1m)
        {
            throw Invalid($"'{name}.confidence' ist keine Zahl zwischen 0 und 1");
        }

        return Math.Round(confidence, 2, MidpointRounding.AwayFromZero);
    }

    private static List<string> ParseUrls(string name, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"'{name}.sourceUrls' ist keine Liste");
        }

        var urls = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw Invalid($"'{name}.sourceUrls' enthält einen Wert, der kein Text ist");
            }

            urls.Add(item.GetString()!);
        }

        return urls;
    }

    private static void RequireExactProperties(JsonElement element, IEnumerable<string> expected, string path)
    {
        var names = element.EnumerateObject().Select(p => p.Name).ToList();
        var expectedNames = expected.ToList();
        if (names.Count != names.Distinct(StringComparer.Ordinal).Count()
            || names.Count != expectedNames.Count
            || !expectedNames.All(names.Contains))
        {
            throw Invalid($"'{path}' hat nicht genau die erwarteten Eigenschaften");
        }
    }

    /// <summary>Models like to wrap JSON in a Markdown code block despite the instruction.</summary>
    private static string StripCodeFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal) || !trimmed.EndsWith("```", StringComparison.Ordinal) || trimmed.Length < 6)
        {
            return trimmed;
        }

        var inner = trimmed[3..^3];
        var newline = inner.IndexOf('\n');
        return newline < 0 ? inner.Trim() : inner[(newline + 1)..].Trim();
    }

    private static EnrichmentProviderException Invalid(string reason) =>
        new($"Die Antwort des Agents entspricht nicht dem erwarteten Schema ({reason}).");
}
