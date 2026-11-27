using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Enrichment;

namespace SoloCrm.Infrastructure.Enrichment;

/// <summary>
/// The answer format the app expects from the agent (ADR-016, point 5). The schema lives in the code, not in the agent:
/// it is part of every request and the answer is validated against it strictly by <see cref="EnrichmentResponseParser"/>.
/// </summary>
internal static class EnrichmentSchema
{
    /// <summary>JSON property name of each field in the answer.</summary>
    public static readonly IReadOnlyDictionary<string, EnrichmentField> Fields = new Dictionary<string, EnrichmentField>
    {
        ["address"] = EnrichmentField.Address,
        ["phone"] = EnrichmentField.Phone,
        ["email"] = EnrichmentField.Email,
        ["legalForm"] = EnrichmentField.LegalForm,
        ["registerCourt"] = EnrichmentField.RegisterCourt,
        ["registerNumber"] = EnrichmentField.RegisterNumber,
        ["managingDirectors"] = EnrichmentField.ManagingDirectors,
        ["vatId"] = EnrichmentField.VatId,
        ["companyProfile"] = EnrichmentField.CompanyProfile,
    };

    public static readonly IReadOnlyList<string> AddressParts = ["street", "street2", "postalCode", "city", "region", "countryCode"];

    public const string ValueKey = "value";
    public const string SourceUrlsKey = "sourceUrls";
    public const string ConfidenceKey = "confidence";

    private const string Schema = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["address", "phone", "email", "legalForm", "registerCourt", "registerNumber", "managingDirectors", "vatId", "companyProfile"],
          "properties": {
            "address": { "anyOf": [{ "type": "null" }, { "$ref": "#/$defs/address" }] },
            "phone": { "$ref": "#/$defs/text" },
            "email": { "$ref": "#/$defs/text" },
            "legalForm": { "$ref": "#/$defs/text" },
            "registerCourt": { "$ref": "#/$defs/text" },
            "registerNumber": { "$ref": "#/$defs/text" },
            "managingDirectors": { "$ref": "#/$defs/text" },
            "vatId": { "$ref": "#/$defs/text" },
            "companyProfile": { "$ref": "#/$defs/text" }
          },
          "$defs": {
            "text": {
              "anyOf": [
                { "type": "null" },
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["value", "sourceUrls", "confidence"],
                  "properties": {
                    "value": { "type": "string" },
                    "sourceUrls": { "type": "array", "items": { "type": "string" } },
                    "confidence": { "type": "number", "minimum": 0, "maximum": 1 }
                  }
                }
              ]
            },
            "address": {
              "type": "object",
              "additionalProperties": false,
              "required": ["value", "sourceUrls", "confidence"],
              "properties": {
                "value": {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["street", "street2", "postalCode", "city", "region", "countryCode"],
                  "properties": {
                    "street": { "type": ["string", "null"] },
                    "street2": { "type": ["string", "null"] },
                    "postalCode": { "type": ["string", "null"] },
                    "city": { "type": ["string", "null"] },
                    "region": { "type": ["string", "null"] },
                    "countryCode": { "type": ["string", "null"] }
                  }
                },
                "sourceUrls": { "type": "array", "items": { "type": "string" } },
                "confidence": { "type": "number", "minimum": 0, "maximum": 1 }
              }
            }
          }
        }
        """;

    /// <summary>
    /// The only input of the agent: the company name, the domain if known, and the answer format. Nothing else from
    /// the CRM is included (ADR-016, point 8).
    /// </summary>
    public static string BuildInput(EnrichmentSubject subject)
    {
        var domain = string.IsNullOrWhiteSpace(subject.Domain) ? "" : $"\nDomain: {subject.Domain}";
        return $$"""
            Recherchiere öffentliche Angaben zu dieser Firma, bevorzugt aus dem Impressum und von der eigenen Website.

            Firma: {{subject.Name}}{{domain}}

            Antworte ausschließlich mit einem JSON-Objekt nach diesem JSON-Schema, ohne Markdown-Codeblock und ohne weiteren Text:
            {{Schema}}

            Regeln:
            - Ist ein Wert nicht belegt, setze das ganze Feld auf null. Erfinde nichts.
            - sourceUrls enthält nur Seiten, die du für den Wert tatsächlich gefunden und gelesen hast (http oder https).
            - confidence ist deine Sicherheit von 0 bis 1.
            - address.countryCode ist ein ISO-3166-1-Alpha-2-Code, z. B. DE.
            - managingDirectors ist eine kommagetrennte Liste der Geschäftsführer.
            - companyProfile ist Markdown: Branche, Größe, Leistungen, Gründungsjahr und eine Kurzbeschreibung, soweit belegt. Gib hier alle Quellen an.
            """;
    }
}
