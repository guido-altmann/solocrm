using SoloCrm.Domain.Common;
using SoloCrm.Domain.Enrichment;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Application.Features.Enrichment;

/// <summary>Readable German texts and the live value of suggestion fields (shared by the suggestion list and the timeline).</summary>
public static class EnrichmentValues
{
    public static string Label(EnrichmentField field) => field switch
    {
        EnrichmentField.Address => "Adresse",
        EnrichmentField.Phone => "Telefon",
        EnrichmentField.Email => "E-Mail",
        EnrichmentField.LegalForm => "Rechtsform",
        EnrichmentField.RegisterCourt => "Registergericht",
        EnrichmentField.RegisterNumber => "Registernummer",
        EnrichmentField.ManagingDirectors => "Geschäftsführung",
        EnrichmentField.VatId => "USt-IdNr.",
        EnrichmentField.CompanyProfile => "Firmenprofil",
        _ => field.ToString(),
    };

    /// <summary>
    /// The value as one readable line: the address JSON as „Straße, PLZ Ort, Land“, everything else unchanged.
    /// </summary>
    public static string Display(EnrichmentField field, string value)
    {
        if (field != EnrichmentField.Address)
        {
            return value;
        }

        return EnrichmentAddress.TryParse(value) is { } address ? Display(address.ToAddress()) : value;
    }

    /// <summary>The value of the organization today, as stored in a suggestion; <c>null</c> if the field is empty.</summary>
    public static string? LiveValue(Organization organization, EnrichmentField field)
    {
        ArgumentNullException.ThrowIfNull(organization);

        return field switch
        {
            EnrichmentField.Address => EnrichmentAddress.FromAddress(organization.Address)?.ToJson(),
            EnrichmentField.Phone => organization.Phone,
            EnrichmentField.Email => organization.Email,
            EnrichmentField.CompanyProfile => null,
            _ => EnrichmentFieldKeys.ExtraFieldKey(field) is { } key && organization.ExtraFields.TryGetValue(key, out var value)
                ? value
                : null,
        };
    }

    /// <summary>The distinct host names of the sources without <c>www.</c>, e.g. „example.com, handelsregister.de“.</summary>
    public static string SourceHosts(IEnumerable<string> sourceUrls)
    {
        ArgumentNullException.ThrowIfNull(sourceUrls);

        var hosts = sourceUrls
            .Select(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) ? uri.Host : null)
            .Where(h => !string.IsNullOrEmpty(h))
            .Select(h => h!.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? h[4..] : h)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return string.Join(", ", hosts);
    }

    private static string Display(Address address)
    {
        var city = string.Join(' ', new[] { address.PostalCode, address.City }.Where(p => !string.IsNullOrWhiteSpace(p)));
        return string.Join(
            ", ",
            new[] { address.Street, address.Street2, city, address.Region, address.CountryCode }
                .Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}
