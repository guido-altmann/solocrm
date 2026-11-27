using System.Text.Json.Nodes;

namespace SoloCrm.IntegrationTests.Enrichment;

/// <summary>Builds answers of the agent in the schema of <c>EnrichmentSchema</c>.</summary>
internal static class AnswerJson
{
    public const string Imprint = "https://example.test/impressum";

    public static JsonObject Field(JsonNode? value, decimal confidence = 0.9m, params string[] urls) => new()
    {
        ["value"] = value,
        ["sourceUrls"] = new JsonArray(urls.Select(u => (JsonNode?)JsonValue.Create(u)).ToArray()),
        ["confidence"] = confidence,
    };

    public static JsonObject Address(
        string? street = "Musterstraße 1", string? postalCode = "10115", string? city = "Berlin", string? countryCode = "DE") => new()
    {
        ["street"] = street,
        ["street2"] = null,
        ["postalCode"] = postalCode,
        ["city"] = city,
        ["region"] = null,
        ["countryCode"] = countryCode,
    };

    /// <summary>An answer in which every field is <c>null</c>; <paramref name="fields"/> replace single fields.</summary>
    public static JsonObject Answer(params (string Name, JsonNode? Value)[] fields)
    {
        var answer = new JsonObject
        {
            ["address"] = null,
            ["phone"] = null,
            ["email"] = null,
            ["legalForm"] = null,
            ["registerCourt"] = null,
            ["registerNumber"] = null,
            ["managingDirectors"] = null,
            ["vatId"] = null,
            ["companyProfile"] = null,
        };
        foreach (var (name, value) in fields)
        {
            answer[name] = value;
        }

        return answer;
    }
}
