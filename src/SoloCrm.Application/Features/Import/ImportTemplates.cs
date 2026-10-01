namespace SoloCrm.Application.Features.Import;

/// <summary>
/// Suggested mappings: the HubSpot contact export (US-16 AK4, iteration 5 decision 9) and a generic one based on common
/// German and English column names. The first matching column wins; header comparison ignores case and surrounding
/// whitespace.
/// </summary>
public static class ImportTemplates
{
    private static readonly (ImportField Field, string Column, string? Fallback)[] HubSpotColumns =
    [
        (ImportField.FirstName, "First Name", null),
        (ImportField.LastName, "Last Name", null),
        (ImportField.Email, "Email", "Work email"),
        (ImportField.Phone, "Phone Number", "Mobile Phone Number"),
        (ImportField.JobTitle, "Job Title", "lh_current_position"),
        (ImportField.LinkedInUrl, "LinkedIn URL", "lh_linkedin_profile_url"),
        (ImportField.Street, "Street Address", null),
        (ImportField.PostalCode, "Postal Code", null),
        (ImportField.City, "City", null),
        (ImportField.Region, "State/Region", null),
        (ImportField.Country, "Country/Region Code", "Country/Region"),
        (ImportField.Organization, "Company Name", null),
        (ImportField.OrganizationWebsite, "Website URL", null),
        (ImportField.Source, "Original Traffic Source", null),
        (ImportField.Tag, "Lifecycle Stage", null),
        (ImportField.HubSpotRecordId, "Record ID", null),
    ];

    private static readonly Dictionary<ImportField, string[]> GenericNames = new()
    {
        [ImportField.FirstName] = ["First Name", "FirstName", "Vorname"],
        [ImportField.LastName] = ["Last Name", "LastName", "Nachname", "Name"],
        [ImportField.Email] = ["Email", "E-Mail", "Mail", "Email Address", "E-Mail-Adresse"],
        [ImportField.Phone] = ["Phone", "Phone Number", "Telefon", "Telefonnummer", "Mobile", "Mobil"],
        [ImportField.JobTitle] = ["Job Title", "Position", "Rolle", "Funktion"],
        [ImportField.LinkedInUrl] = ["LinkedIn", "LinkedIn URL", "LinkedIn-Profil"],
        [ImportField.Street] = ["Straße", "Strasse", "Street", "Street Address", "Adresse", "Anschrift"],
        [ImportField.Street2] = ["Adresszusatz", "Street Address 2", "Address 2"],
        [ImportField.PostalCode] = ["PLZ", "Postleitzahl", "Postal Code", "Zip", "ZIP Code"],
        [ImportField.City] = ["Ort", "Stadt", "City"],
        [ImportField.Region] = ["Bundesland", "Region", "State", "State/Region"],
        [ImportField.Country] = ["Land", "Country", "Country/Region", "Country/Region Code"],
        [ImportField.Organization] = ["Company", "Company Name", "Firma", "Organisation", "Organization", "Unternehmen"],
        [ImportField.OrganizationWebsite] = ["Website", "Website URL", "Webseite"],
        [ImportField.Source] = ["Source", "Quelle"],
        [ImportField.Tag] = ["Tag", "Tags"],
        [ImportField.HubSpotRecordId] = ["Record ID"],
    };

    /// <summary>HubSpot when the typical columns are present, otherwise generic.</summary>
    public static ImportTemplate Detect(IReadOnlyList<string> headers) =>
        IndexOf(headers, "Record ID") is not null && IndexOf(headers, "Lifecycle Stage") is not null
            ? ImportTemplate.HubSpot
            : ImportTemplate.Generic;

    public static ImportMapping Suggest(ImportTemplate template, IReadOnlyList<string> headers) => template == ImportTemplate.HubSpot
        ? new ImportMapping(
            [.. HubSpotColumns.Select(c => new FieldMapping(c.Field, IndexOf(headers, c.Column), c.Fallback is null ? null : IndexOf(headers, c.Fallback)))],
            ImportTemplate.HubSpot)
        : new ImportMapping(
            [.. Enum.GetValues<ImportField>().Select(f => new FieldMapping(f, GenericNames[f].Select(n => IndexOf(headers, n)).FirstOrDefault(i => i is not null)))],
            ImportTemplate.Generic);

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
}
