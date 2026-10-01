using SoloCrm.Domain.Common;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Application.Features.Import;

/// <summary>What a CSV file is imported as (iteration 5 decision 15).</summary>
public enum ImportTarget
{
    Contacts,
    Organizations,
}

/// <summary>
/// Target fields of the import (US-16, iteration 5 decisions 9, 14–16). <see cref="ImportFields.For"/> lists the fields of
/// each <see cref="ImportTarget"/>; the address fields are shared.
/// </summary>
public enum ImportField
{
    FirstName,
    LastName,
    Email,
    Phone,
    JobTitle,
    LinkedInUrl,
    Street,
    Street2,
    PostalCode,
    City,
    Region,

    /// <summary>ISO code, German or English name (<see cref="Countries.Find"/>).</summary>
    Country,

    /// <summary>
    /// Contacts: name of the employer, an existing organization (case-insensitive) or a new one with type <c>Other</c>.
    /// Organizations: the name of the organization itself.
    /// </summary>
    Organization,

    /// <summary>Organizations: the type (<see cref="OrganizationTypeValues"/>).</summary>
    OrganizationType,

    /// <summary>
    /// Contacts: website of an organization that the import creates (existing ones stay unchanged).
    /// Organizations: the website.
    /// </summary>
    OrganizationWebsite,

    /// <summary>Contacts: HubSpot record id of the employer; links to the organization imported with that id (decision 16).</summary>
    HubSpotCompanyId,

    Source,

    /// <summary>The column value becomes a tag (existing tags are reused regardless of case).</summary>
    Tag,

    /// <summary>Stored in <c>ExtraFields</c>; identifies records on a repeated import.</summary>
    HubSpotRecordId,
}

public static class ImportFields
{
    private static readonly ImportField[] Address =
        [ImportField.Street, ImportField.Street2, ImportField.PostalCode, ImportField.City, ImportField.Region, ImportField.Country];

    private static readonly ImportField[] Contacts =
    [
        ImportField.FirstName, ImportField.LastName, ImportField.Email, ImportField.Phone, ImportField.JobTitle, ImportField.LinkedInUrl,
        .. Address,
        ImportField.Organization, ImportField.OrganizationWebsite, ImportField.HubSpotCompanyId, ImportField.Source, ImportField.Tag,
        ImportField.HubSpotRecordId,
    ];

    private static readonly ImportField[] Organizations =
    [
        ImportField.Organization, ImportField.OrganizationType, ImportField.OrganizationWebsite,
        .. Address,
        ImportField.Tag, ImportField.HubSpotRecordId,
    ];

    /// <summary>The target fields of <paramref name="target"/> in display order.</summary>
    public static IReadOnlyList<ImportField> For(ImportTarget target) => target == ImportTarget.Organizations ? Organizations : Contacts;
}

/// <summary>How values like <see cref="ImportField.Source"/> or <see cref="ImportField.OrganizationType"/> are interpreted.</summary>
public enum ImportTemplate
{
    /// <summary>Names of the enum values or their German labels; anything else is <c>Other</c>.</summary>
    Generic,

    /// <summary>HubSpot exports: <c>Original Traffic Source</c> per decision 9 A, company <c>Type</c> per decision 15.</summary>
    HubSpot,
}

public enum DuplicateHandling
{
    Skip,

    /// <summary>Overwrites the existing record with the non-empty values of the row.</summary>
    Update,
}

/// <summary>One target field: the source column and an optional column used when the first one is empty (by index).</summary>
public sealed record FieldMapping(ImportField Field, int? Column, int? FallbackColumn = null);

public sealed record ImportMapping(
    IReadOnlyList<FieldMapping> Fields,
    ImportTemplate Template = ImportTemplate.Generic,
    ImportTarget Target = ImportTarget.Contacts)
{
    public const string HubSpotRecordIdKey = "HubSpotRecordId";

    public FieldMapping? For(ImportField field) => Fields.FirstOrDefault(f => f.Field == field && f.Column is not null);

    /// <summary>The value of <paramref name="field"/> in <paramref name="row"/>, falling back to the second column.</summary>
    public string? Value(CsvRow row, ImportField field)
    {
        ArgumentNullException.ThrowIfNull(row);
        return For(field) is { } mapping ? row.Get(mapping.Column) ?? row.Get(mapping.FallbackColumn) : null;
    }

    /// <summary>Maps a source value; <c>null</c> keeps the source empty (blank or deliberately ignored values).</summary>
    public LeadSource? SourceOf(string? value) => Template == ImportTemplate.HubSpot
        ? SourceValues.FromHubSpot(value)
        : SourceValues.FromGeneric(value);

    /// <summary>Maps an organization type; <c>null</c> for a blank value.</summary>
    public OrganizationType? OrganizationTypeOf(string? value) => Template == ImportTemplate.HubSpot
        ? OrganizationTypeValues.FromHubSpot(value)
        : OrganizationTypeValues.FromGeneric(value);
}

/// <summary>Value mapping of organization types (iteration 5 decision 15).</summary>
public static class OrganizationTypeValues
{
    private static readonly Dictionary<string, OrganizationType> HubSpot = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PROSPECT"] = OrganizationType.Client,
        ["PARTNER"] = OrganizationType.Partner,
        ["RESELLER"] = OrganizationType.Agency,
    };

    private static readonly Dictionary<string, OrganizationType> GermanLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Endkunde"] = OrganizationType.Client,
        ["Kunde"] = OrganizationType.Client,
        ["Vermittler"] = OrganizationType.Agency,
        ["Agentur"] = OrganizationType.Agency,
        ["Sonstige"] = OrganizationType.Other,
    };

    /// <summary>Label (<c>Prospect</c>) or internal value (<c>PROSPECT</c>); Vendor, Other and unknown values become <c>Other</c>.</summary>
    public static OrganizationType? FromHubSpot(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : HubSpot.GetValueOrDefault(value.Trim(), OrganizationType.Other);

    public static OrganizationType? FromGeneric(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (Enum.TryParse<OrganizationType>(trimmed, ignoreCase: true, out var type) && Enum.IsDefined(type)
            && !int.TryParse(trimmed, out _))
        {
            return type;
        }

        return GermanLabels.GetValueOrDefault(trimmed, OrganizationType.Other);
    }
}

/// <summary>Value mapping of lead sources (iteration 5 decision 9 A).</summary>
public static class SourceValues
{
    private static readonly Dictionary<string, LeadSource?> HubSpot = new(StringComparer.OrdinalIgnoreCase)
    {
        ["REFERRALS"] = LeadSource.Referral,
        ["ORGANIC_SEARCH"] = LeadSource.Website,
        ["PAID_SEARCH"] = LeadSource.Website,
        ["DIRECT_TRAFFIC"] = LeadSource.Website,
        ["AI_REFERRALS"] = LeadSource.Website,

        // Not used for leads; the source stays empty.
        ["SOCIAL_MEDIA"] = null,
        ["PAID_SOCIAL"] = null,
    };

    private static readonly Dictionary<string, LeadSource> GermanLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Projektportal"] = LeadSource.ProjectPortal,
        ["Empfehlung"] = LeadSource.Referral,
        ["Webseite"] = LeadSource.Website,
        ["Veranstaltung"] = LeadSource.Event,
        ["Sonstige"] = LeadSource.Other,
    };

    /// <summary>Display names (<c>Organic Search</c>) and internal names (<c>ORGANIC_SEARCH</c>) are both recognized.</summary>
    public static LeadSource? FromHubSpot(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var key = string.Join('_', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return HubSpot.TryGetValue(key, out var source) ? source : LeadSource.Other;
    }

    public static LeadSource? FromGeneric(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (Enum.TryParse<LeadSource>(trimmed, ignoreCase: true, out var source) && Enum.IsDefined(source) && !int.TryParse(trimmed, out _))
        {
            return source;
        }

        return GermanLabels.TryGetValue(trimmed, out var label) ? label : LeadSource.Other;
    }
}
