namespace SoloCrm.Application.Features.Import;

/// <summary>
/// Suggested mappings: the HubSpot contact and company exports (US-16 AK4/AK5, iteration 5 decisions 9, 14–16) and
/// generic ones based on common German and English column names. For each target field the first existing column of the
/// candidate list wins; header comparison ignores case and surrounding whitespace.
/// </summary>
public static class ImportTemplates
{
    /// <summary>
    /// Column of the employer's HubSpot record id in the contact export. Not confirmed with a real export yet (step 12),
    /// hence several candidates; HubSpot lists several ids separated by <c>;</c>, the import uses the first.
    /// </summary>
    private static readonly string[] HubSpotCompanyIdColumns =
        ["Associated Company IDs (Primary)", "Primary Associated Company ID", "Associated Company IDs", "Associated Company ID"];

    private static readonly ColumnSpec[] HubSpotContactColumns =
    [
        new(ImportField.FirstName, ["First Name"]),
        new(ImportField.LastName, ["Last Name"]),
        new(ImportField.Email, ["Email"], ["Work email"]),
        new(ImportField.Phone, ["Phone Number"], ["Mobile Phone Number"]),
        new(ImportField.JobTitle, ["Job Title"], ["lh_current_position"]),
        new(ImportField.LinkedInUrl, ["LinkedIn URL"], ["lh_linkedin_profile_url"]),
        new(ImportField.Street, ["Street Address"]),
        new(ImportField.PostalCode, ["Postal Code"]),
        new(ImportField.City, ["City"]),
        new(ImportField.Region, ["State/Region"]),
        new(ImportField.Country, ["Country/Region Code"], ["Country/Region"]),
        new(ImportField.Organization, ["Company Name"]),
        new(ImportField.OrganizationWebsite, ["Website URL"]),
        new(ImportField.HubSpotCompanyId, HubSpotCompanyIdColumns),
        new(ImportField.Source, ["Original Traffic Source"]),
        new(ImportField.Tag, ["Lifecycle Stage"]),
        new(ImportField.HubSpotRecordId, ["Record ID"]),
    ];

    /// <summary>Labels of the HubSpot company properties (checked against the account's property definitions).</summary>
    private static readonly ColumnSpec[] HubSpotCompanyColumns =
    [
        new(ImportField.Organization, ["Company name"]),
        new(ImportField.OrganizationType, ["Type"]),
        new(ImportField.OrganizationWebsite, ["Website URL"], ["Company Domain Name"]),
        new(ImportField.Street, ["Street Address"]),
        new(ImportField.Street2, ["Street Address 2"]),
        new(ImportField.PostalCode, ["Postal Code"]),
        new(ImportField.City, ["City"]),
        new(ImportField.Region, ["State/Region"]),
        new(ImportField.Country, ["Country/Region Code"], ["Country/Region"]),
        new(ImportField.Tag, ["Lifecycle Stage"]),
        new(ImportField.HubSpotRecordId, ["Record ID"]),
    ];

    private static readonly string[] PersonColumns = ["First Name", "FirstName", "Vorname", "Last Name", "LastName", "Nachname", "Email", "E-Mail"];

    private static readonly string[] OrganizationNameColumns = ["Company name", "Company", "Firma", "Organisation", "Organization", "Unternehmen"];

    private static readonly Dictionary<ImportField, string[]> GenericAddressNames = new()
    {
        [ImportField.Street] = ["Straße", "Strasse", "Street", "Street Address", "Adresse", "Anschrift"],
        [ImportField.Street2] = ["Adresszusatz", "Street Address 2", "Address 2"],
        [ImportField.PostalCode] = ["PLZ", "Postleitzahl", "Postal Code", "Zip", "ZIP Code"],
        [ImportField.City] = ["Ort", "Stadt", "City"],
        [ImportField.Region] = ["Bundesland", "Region", "State", "State/Region"],
        [ImportField.Country] = ["Land", "Country", "Country/Region", "Country/Region Code"],
    };

    private static readonly Dictionary<ImportField, string[]> GenericContactNames = new(GenericAddressNames)
    {
        [ImportField.FirstName] = ["First Name", "FirstName", "Vorname"],
        [ImportField.LastName] = ["Last Name", "LastName", "Nachname", "Name"],
        [ImportField.Email] = ["Email", "E-Mail", "Mail", "Email Address", "E-Mail-Adresse"],
        [ImportField.Phone] = ["Phone", "Phone Number", "Telefon", "Telefonnummer", "Mobile", "Mobil"],
        [ImportField.JobTitle] = ["Job Title", "Position", "Rolle", "Funktion"],
        [ImportField.LinkedInUrl] = ["LinkedIn", "LinkedIn URL", "LinkedIn-Profil"],
        [ImportField.Organization] = ["Company", "Company Name", "Firma", "Organisation", "Organization", "Unternehmen"],
        [ImportField.OrganizationWebsite] = ["Website", "Website URL", "Webseite"],
        [ImportField.HubSpotCompanyId] = ["Firmen-ID", "Company ID", .. HubSpotCompanyIdColumns],
        [ImportField.Source] = ["Source", "Quelle"],
        [ImportField.Tag] = ["Tag", "Tags"],
        [ImportField.HubSpotRecordId] = ["Record ID"],
    };

    private static readonly Dictionary<ImportField, string[]> GenericOrganizationNames = new(GenericAddressNames)
    {
        [ImportField.Organization] = ["Name", .. OrganizationNameColumns],
        [ImportField.OrganizationType] = ["Typ", "Type", "Art"],
        [ImportField.OrganizationWebsite] = ["Website", "Website URL", "Webseite", "Domain", "Company Domain Name"],
        [ImportField.Tag] = ["Tag", "Tags"],
        [ImportField.HubSpotRecordId] = ["Record ID"],
    };

    /// <summary>
    /// Persons when name or email columns exist, otherwise organizations when an organization name column exists;
    /// HubSpot when the export has its <c>Record ID</c> column.
    /// </summary>
    public static (ImportTarget Target, ImportTemplate Template) Detect(IReadOnlyList<string> headers)
    {
        var target = PersonColumns.Any(c => IndexOf(headers, c) is not null) || OrganizationNameColumns.All(c => IndexOf(headers, c) is null)
            ? ImportTarget.Contacts
            : ImportTarget.Organizations;
        var template = IndexOf(headers, "Record ID") is not null ? ImportTemplate.HubSpot : ImportTemplate.Generic;
        return (target, template);
    }

    public static ImportMapping Suggest(ImportTarget target, ImportTemplate template, IReadOnlyList<string> headers)
    {
        if (template == ImportTemplate.HubSpot)
        {
            var columns = target == ImportTarget.Organizations ? HubSpotCompanyColumns : HubSpotContactColumns;
            return new ImportMapping([.. columns.Select(c => Map(c, headers))], template, target);
        }

        var names = target == ImportTarget.Organizations ? GenericOrganizationNames : GenericContactNames;
        return new ImportMapping(
            [.. ImportFields.For(target).Select(f => new FieldMapping(f, names.TryGetValue(f, out var candidates) ? First(headers, candidates) : null))],
            template,
            target);
    }

    /// <summary>Without the main column, the fallback column becomes the main one (e.g. only „Company Domain Name“).</summary>
    private static FieldMapping Map(ColumnSpec spec, IReadOnlyList<string> headers)
    {
        var column = First(headers, spec.Columns);
        var fallback = spec.Fallbacks is null ? null : First(headers, spec.Fallbacks);
        return column is null ? new FieldMapping(spec.Field, fallback) : new FieldMapping(spec.Field, column, fallback);
    }

    private static int? First(IReadOnlyList<string> headers, IEnumerable<string> candidates) =>
        candidates.Select(c => IndexOf(headers, c)).FirstOrDefault(i => i is not null);

    private static int? IndexOf(IReadOnlyList<string> headers, string name)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            if (string.Equals(headers[i].Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return null;
    }

    private sealed record ColumnSpec(ImportField Field, string[] Columns, string[]? Fallbacks = null);
}
