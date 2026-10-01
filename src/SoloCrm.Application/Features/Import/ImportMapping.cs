using SoloCrm.Domain.Common;

namespace SoloCrm.Application.Features.Import;

/// <summary>Target fields of the contact import (US-16, iteration 5 decision 9).</summary>
public enum ImportField
{
    FirstName,
    LastName,
    Email,
    Phone,
    JobTitle,
    LinkedInUrl,

    /// <summary>Name of an existing organization (case-insensitive) or of a new one with type <c>Other</c>.</summary>
    Organization,

    /// <summary>Website of an organization that the import creates; existing organizations stay unchanged.</summary>
    OrganizationWebsite,

    Source,

    /// <summary>The column value becomes a tag (existing tags are reused regardless of case).</summary>
    Tag,

    /// <summary>Stored in <c>ExtraFields</c>; identifies contacts without email on a repeated import.</summary>
    HubSpotRecordId,
}

/// <summary>How values of the <see cref="ImportField.Source"/> column are interpreted.</summary>
public enum ImportTemplate
{
    /// <summary>Lead source names (<c>LinkedIn</c>, <c>Referral</c> …) or their German labels; anything else is <c>Other</c>.</summary>
    Generic,

    /// <summary>HubSpot export: <c>Original Traffic Source</c> per decision 9 A.</summary>
    HubSpot,
}

public enum DuplicateHandling
{
    Skip,

    /// <summary>Overwrites the existing contact with the non-empty values of the row.</summary>
    Update,
}

/// <summary>One target field: the source column and an optional column used when the first one is empty (by index).</summary>
public sealed record FieldMapping(ImportField Field, int? Column, int? FallbackColumn = null);

public sealed record ImportMapping(IReadOnlyList<FieldMapping> Fields, ImportTemplate Template = ImportTemplate.Generic)
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
