using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.Organizations;

/// <summary>
/// A company: end client, agency, partner or other (SPEC 2.3).
/// </summary>
public sealed class Organization : ArchivableEntity, IAuditable, IHasExtraFields
{
    public const int NameMaxLength = 200;
    public const int WebsiteMaxLength = 500;
    public const int CityMaxLength = 100;

    // Required by EF Core.
    private Organization()
    {
        Name = null!;
    }

    private Organization(string name)
    {
        Name = name;
    }

    public string Name { get; private set; }

    public OrganizationType Type { get; private set; } = OrganizationType.Other;

    /// <summary>Normalized, see <see cref="Website.TryNormalize"/>.</summary>
    public string? Website { get; private set; }

    public string? City { get; private set; }

    /// <summary>Free-text master note; the history lives in activities.</summary>
    public string? Notes { get; private set; }

    public IReadOnlyDictionary<string, string> ExtraFields { get; private set; } = new Dictionary<string, string>();

    public static Organization Create(
        string name,
        OrganizationType type = OrganizationType.Other,
        string? website = null,
        string? city = null,
        string? notes = null)
    {
        var organization = new Organization(RequireName(name));
        organization.Apply(type, website, city, notes);
        organization.AddDomainEvent(new OrganizationCreated(organization.Id));
        return organization;
    }

    public void Update(string name, OrganizationType type, string? website, string? city, string? notes)
    {
        Name = RequireName(name);
        Apply(type, website, city, notes);
    }

    private void Apply(OrganizationType type, string? website, string? city, string? notes)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown organization type.");
        }

        Type = type;
        Website = NormalizeWebsite(website);
        City = Normalize(city);
        Notes = Normalize(notes);
    }

    private static string RequireName(string name) =>
        Normalize(name) ?? throw new ArgumentException("An organization requires a name.", nameof(name));

    private static string? NormalizeWebsite(string? website)
    {
        if (string.IsNullOrWhiteSpace(website))
        {
            return null;
        }

        return Organizations.Website.TryNormalize(website, out var normalized)
            ? normalized
            : throw new ArgumentException("The website is not a valid http(s) URL.", nameof(website));
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
