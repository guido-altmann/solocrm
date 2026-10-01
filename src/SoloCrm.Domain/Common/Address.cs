namespace SoloCrm.Domain.Common;

/// <summary>
/// Postal address of a contact or an organization (SPEC 2.3, iteration 5 decision 14). Immutable; mapped as a required
/// EF Core complex type, so a missing address is <see cref="Empty"/> rather than <c>null</c>: with only optional members,
/// a nullable complex type could not tell „no address“ from „address with empty fields“.
/// </summary>
public sealed record Address
{
    public const int StreetMaxLength = 200;
    public const int PostalCodeMaxLength = 20;
    public const int CityMaxLength = 100;
    public const int RegionMaxLength = 100;

    // Required by EF Core and for Empty.
    private Address()
    {
    }

    public static Address Empty { get; } = new();

    /// <summary>Street and house number.</summary>
    public string? Street { get; private init; }

    /// <summary>Additional line, e.g. c/o, building or PO box.</summary>
    public string? Street2 { get; private init; }

    public string? PostalCode { get; private init; }

    public string? City { get; private init; }

    /// <summary>State, canton or region.</summary>
    public string? Region { get; private init; }

    /// <summary>ISO 3166-1 alpha-2 code from <see cref="Countries"/>, upper case.</summary>
    public string? CountryCode { get; private init; }

    public bool IsEmpty => this == Empty;

    /// <summary>Trims all values and stores blanks as <c>null</c>; returns <see cref="Empty"/> when nothing is left.</summary>
    /// <exception cref="ArgumentException">A value is too long or the country code is unknown.</exception>
    public static Address Create(
        string? street = null,
        string? street2 = null,
        string? postalCode = null,
        string? city = null,
        string? region = null,
        string? countryCode = null)
    {
        var country = Normalize(countryCode)?.ToUpperInvariant();
        if (country is not null && !Countries.IsValidCode(country))
        {
            throw new ArgumentException($"'{country}' is not an ISO 3166-1 alpha-2 country code.", nameof(countryCode));
        }

        var address = new Address
        {
            Street = Require(street, StreetMaxLength, nameof(street)),
            Street2 = Require(street2, StreetMaxLength, nameof(street2)),
            PostalCode = Require(postalCode, PostalCodeMaxLength, nameof(postalCode)),
            City = Require(city, CityMaxLength, nameof(city)),
            Region = Require(region, RegionMaxLength, nameof(region)),
            CountryCode = country,
        };
        return address == Empty ? Empty : address;
    }

    /// <summary>
    /// Lines as written on an envelope in German usage: street, additional line, „PLZ Ort“, region, country (German name).
    /// </summary>
    public IReadOnlyList<string> ToLines()
    {
        var lines = new List<string>(5);
        AddIfPresent(lines, Street);
        AddIfPresent(lines, Street2);
        AddIfPresent(lines, string.Join(' ', new[] { PostalCode, City }.Where(v => v is not null)));
        AddIfPresent(lines, Region);
        AddIfPresent(lines, Countries.ByIsoCode(CountryCode)?.GermanName);
        return lines;
    }

    private static void AddIfPresent(List<string> lines, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            lines.Add(value);
        }
    }

    private static string? Require(string? value, int maxLength, string name)
    {
        var normalized = Normalize(value);
        return normalized is null || normalized.Length <= maxLength
            ? normalized
            : throw new ArgumentException($"At most {maxLength} characters.", name);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
