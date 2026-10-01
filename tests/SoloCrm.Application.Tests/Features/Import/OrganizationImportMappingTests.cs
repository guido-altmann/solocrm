using SoloCrm.Application.Features.Import;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Application.Tests.Features.Import;

public sealed class OrganizationImportMappingTests
{
    /// <summary>Labels of the HubSpot company properties, in the order of a (shortened) export.</summary>
    private static readonly string[] HubSpotCompanyHeader =
    [
        "Record ID", "Company name", "Company Domain Name", "Website URL", "Type", "Phone Number", "Street Address", "Street Address 2",
        "Postal Code", "City", "State/Region", "Country/Region", "Country/Region Code", "Lifecycle Stage", "Create Date",
    ];

    [Theory]
    [InlineData("Prospect", OrganizationType.Client)]
    [InlineData("PROSPECT", OrganizationType.Client)]
    [InlineData("Partner", OrganizationType.Partner)]
    [InlineData("RESELLER", OrganizationType.Agency)]
    [InlineData("Vendor", OrganizationType.Other)]
    [InlineData("Other", OrganizationType.Other)]
    [InlineData("Something new", OrganizationType.Other)]
    [InlineData(" ", null)]
    public void FromHubSpot_Type_MapsPerDecision15(string value, OrganizationType? expected)
    {
        OrganizationTypeValues.FromHubSpot(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("Client", OrganizationType.Client)]
    [InlineData("Endkunde", OrganizationType.Client)]
    [InlineData("vermittler", OrganizationType.Agency)]
    [InlineData("Partner", OrganizationType.Partner)]
    [InlineData("2", OrganizationType.Other)]
    [InlineData("", null)]
    public void FromGeneric_Type_MapsNamesAndLabels(string value, OrganizationType? expected)
    {
        OrganizationTypeValues.FromGeneric(value).Should().Be(expected);
    }

    [Fact]
    public void Detect_HubSpotCompanyExport_ReturnsOrganizationsAndHubSpot()
    {
        ImportTemplates.Detect(HubSpotCompanyHeader).Should().Be((ImportTarget.Organizations, ImportTemplate.HubSpot));
    }

    [Fact]
    public void Detect_GenericCompanyList_ReturnsOrganizations()
    {
        ImportTemplates.Detect(["Firma", "PLZ", "Ort"]).Should().Be((ImportTarget.Organizations, ImportTemplate.Generic));
        ImportTemplates.Detect(["Vorname", "Nachname", "Firma"]).Should().Be((ImportTarget.Contacts, ImportTemplate.Generic));
        ImportTemplates.Detect(["Spalte A"]).Should().Be((ImportTarget.Contacts, ImportTemplate.Generic));
    }

    [Fact]
    public void Suggest_HubSpotCompany_MapsAllFieldsWithDomainAndCountryFallbacks()
    {
        var mapping = ImportTemplates.Suggest(ImportTarget.Organizations, ImportTemplate.HubSpot, HubSpotCompanyHeader);

        mapping.Target.Should().Be(ImportTarget.Organizations);
        mapping.Fields.Where(f => f.Column is not null).Should().BeEquivalentTo(new FieldMapping[]
        {
            new(ImportField.Organization, 1),
            new(ImportField.OrganizationType, 4),
            new(ImportField.OrganizationWebsite, 3, 2),
            new(ImportField.Street, 6),
            new(ImportField.Street2, 7),
            new(ImportField.PostalCode, 8),
            new(ImportField.City, 9),
            new(ImportField.Region, 10),
            new(ImportField.Country, 12, 11),
            new(ImportField.Tag, 13),
            new(ImportField.HubSpotRecordId, 0),
        });
    }

    [Fact]
    public void Suggest_OnlyFallbackColumnPresent_UsesItAsMainColumn()
    {
        var mapping = ImportTemplates.Suggest(ImportTarget.Organizations, ImportTemplate.HubSpot, ["Record ID", "Company name", "Company Domain Name"]);

        mapping.For(ImportField.OrganizationWebsite).Should().Be(new FieldMapping(ImportField.OrganizationWebsite, 2));
    }

    [Theory]
    [InlineData("Associated Company IDs (Primary)")]
    [InlineData("Associated Company IDs")]
    [InlineData("Associated Company ID")]
    public void Suggest_HubSpotContact_FindsCompanyIdColumn(string column)
    {
        var mapping = ImportTemplates.Suggest(ImportTarget.Contacts, ImportTemplate.HubSpot, ["Record ID", "First Name", "Last Name", column]);

        mapping.For(ImportField.HubSpotCompanyId)!.Column.Should().Be(3);
    }

    [Fact]
    public void Suggest_HubSpotContact_PrefersPrimaryCompanyIdColumn()
    {
        var mapping = ImportTemplates.Suggest(
            ImportTarget.Contacts, ImportTemplate.HubSpot, ["Record ID", "Associated Company IDs", "Associated Company IDs (Primary)"]);

        mapping.For(ImportField.HubSpotCompanyId)!.Column.Should().Be(2);
    }

    [Fact]
    public void For_Target_ListsOnlyFieldsOfTheTarget()
    {
        ImportFields.For(ImportTarget.Organizations).Should().NotContain([ImportField.FirstName, ImportField.HubSpotCompanyId, ImportField.Source]);
        ImportFields.For(ImportTarget.Contacts).Should().NotContain(ImportField.OrganizationType);
        ImportFields.For(ImportTarget.Contacts).Should().Contain([ImportField.Street, ImportField.Country, ImportField.HubSpotCompanyId]);
    }
}
