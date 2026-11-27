using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using SoloCrm.Domain.Common;

namespace SoloCrm.Application.Features.Enrichment;

/// <summary>
/// The value of an address suggestion: all parts together as one JSON text (SPEC 9.3), so the user accepts or rejects the
/// address as a whole.
/// </summary>
public sealed record EnrichmentAddress(
    string? Street, string? Street2, string? PostalCode, string? City, string? Region, string? CountryCode)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Readable umlauts in the stored text; the value is never embedded in HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Validates and normalizes the parts.</summary>
    /// <exception cref="ArgumentException">A part is too long, the country code is unknown, or nothing is left.</exception>
    public static EnrichmentAddress From(string? street, string? street2, string? postalCode, string? city, string? region, string? countryCode)
    {
        var address = Address.Create(street, street2, postalCode, city, region, countryCode);
        return address.IsEmpty
            ? throw new ArgumentException("The address is empty.")
            : new EnrichmentAddress(address.Street, address.Street2, address.PostalCode, address.City, address.Region, address.CountryCode);
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Reads the stored suggestion value; <c>null</c> if it is not an address.</summary>
    public static EnrichmentAddress? TryParse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<EnrichmentAddress>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public Address ToAddress() => Address.Create(Street, Street2, PostalCode, City, Region, CountryCode);
}
