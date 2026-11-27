using SoloCrm.Domain.Enrichment;

namespace SoloCrm.Application.Features.Enrichment;

/// <summary>Fixed <c>ExtraFields</c> keys of the imprint details (SPEC 9.3) until custom properties exist.</summary>
public static class EnrichmentFieldKeys
{
    public const string LegalForm = "LegalForm";
    public const string RegisterCourt = "RegisterCourt";
    public const string RegisterNumber = "RegisterNumber";
    public const string ManagingDirectors = "ManagingDirectors";
    public const string VatId = "VatId";

    /// <returns>The key, or <c>null</c> for fields that have their own place (address, phone, e-mail, company profile).</returns>
    public static string? ExtraFieldKey(EnrichmentField field) => field switch
    {
        EnrichmentField.LegalForm => LegalForm,
        EnrichmentField.RegisterCourt => RegisterCourt,
        EnrichmentField.RegisterNumber => RegisterNumber,
        EnrichmentField.ManagingDirectors => ManagingDirectors,
        EnrichmentField.VatId => VatId,
        _ => null,
    };
}
