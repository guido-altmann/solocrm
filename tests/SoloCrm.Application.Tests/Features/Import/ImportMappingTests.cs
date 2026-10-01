using System.Text;
using SoloCrm.Application.Features.Import;
using SoloCrm.Domain.Common;

namespace SoloCrm.Application.Tests.Features.Import;

public sealed class ImportMappingTests
{
    [Fact]
    public void Detect_HubSpotHeader_ReturnsHubSpot()
    {
        var document = CsvDocument.Parse(HubSpotSample.File()).Value;

        ImportTemplates.Detect(document.Headers).Should().Be(ImportTemplate.HubSpot);
        ImportTemplates.Detect(["Vorname", "Nachname"]).Should().Be(ImportTemplate.Generic);
    }

    [Fact]
    public void Suggest_HubSpot_MapsColumnsAndFallbacksPerDecision9()
    {
        var headers = CsvDocument.Parse(HubSpotSample.File()).Value.Headers;

        var mapping = ImportTemplates.Suggest(ImportTemplate.HubSpot, headers);

        mapping.Template.Should().Be(ImportTemplate.HubSpot);
        mapping.For(ImportField.Email).Should().Be(new FieldMapping(ImportField.Email, 3, 4));
        mapping.For(ImportField.Phone).Should().Be(new FieldMapping(ImportField.Phone, 5, 6));
        mapping.For(ImportField.JobTitle).Should().Be(new FieldMapping(ImportField.JobTitle, 8, 9));
        mapping.For(ImportField.LinkedInUrl).Should().Be(new FieldMapping(ImportField.LinkedInUrl, 10, 11));
        mapping.For(ImportField.Organization)!.Column.Should().Be(12);
        mapping.For(ImportField.OrganizationWebsite)!.Column.Should().Be(13);
        mapping.For(ImportField.Source)!.Column.Should().Be(14);
        mapping.For(ImportField.Tag)!.Column.Should().Be(15);
        mapping.For(ImportField.HubSpotRecordId)!.Column.Should().Be(0);
    }

    [Fact]
    public void Suggest_GenericGermanHeaders_MapsByCommonNames()
    {
        var mapping = ImportTemplates.Suggest(ImportTemplate.Generic, ["Vorname", "Nachname", "E-Mail", "Firma", "Quelle", "Notiz"]);

        mapping.For(ImportField.FirstName)!.Column.Should().Be(0);
        mapping.For(ImportField.LastName)!.Column.Should().Be(1);
        mapping.For(ImportField.Email)!.Column.Should().Be(2);
        mapping.For(ImportField.Organization)!.Column.Should().Be(3);
        mapping.For(ImportField.Source)!.Column.Should().Be(4);
        mapping.For(ImportField.Phone).Should().BeNull();
    }

    [Fact]
    public void Value_EmptyFirstColumn_UsesFallback()
    {
        var document = CsvDocument.Parse(HubSpotSample.File(
            HubSpotSample.Row("1", email: "", workEmail: "ada@work.test"),
            HubSpotSample.Row("2", email: "grace@example.test", workEmail: "grace@work.test"))).Value;
        var mapping = ImportTemplates.Suggest(ImportTemplate.HubSpot, document.Headers);

        mapping.Value(document.Rows[0], ImportField.Email).Should().Be("ada@work.test");
        mapping.Value(document.Rows[1], ImportField.Email).Should().Be("grace@example.test");
        mapping.Value(document.Rows[0], ImportField.Phone).Should().BeNull();
    }

    [Theory]
    [InlineData("Referrals", LeadSource.Referral)]
    [InlineData("REFERRALS", LeadSource.Referral)]
    [InlineData("Organic Search", LeadSource.Website)]
    [InlineData("PAID_SEARCH", LeadSource.Website)]
    [InlineData("Direct Traffic", LeadSource.Website)]
    [InlineData("AI Referrals", LeadSource.Website)]
    [InlineData("Social Media", null)]
    [InlineData("PAID_SOCIAL", null)]
    [InlineData("Offline Sources", LeadSource.Other)]
    [InlineData("Email Marketing", LeadSource.Other)]
    [InlineData("Other Campaigns", LeadSource.Other)]
    [InlineData("", null)]
    public void FromHubSpot_TrafficSource_MapsPerDecision9A(string value, LeadSource? expected)
    {
        SourceValues.FromHubSpot(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("LinkedIn", LeadSource.LinkedIn)]
    [InlineData("linkedin", LeadSource.LinkedIn)]
    [InlineData("Empfehlung", LeadSource.Referral)]
    [InlineData("Projektportal", LeadSource.ProjectPortal)]
    [InlineData("Messe", LeadSource.Other)]
    [InlineData("3", LeadSource.Other)]
    [InlineData(" ", null)]
    public void FromGeneric_Value_MapsNamesAndLabels(string value, LeadSource? expected)
    {
        SourceValues.FromGeneric(value).Should().Be(expected);
    }

    [Fact]
    public void SourceOf_Template_UsesMatchingValueMapping()
    {
        new ImportMapping([], ImportTemplate.HubSpot).SourceOf("Organic Search").Should().Be(LeadSource.Website);
        new ImportMapping([], ImportTemplate.Generic).SourceOf("Organic Search").Should().Be(LeadSource.Other);
    }

    [Fact]
    public void Parse_GermanExcelExport_WorksWithGenericMapping()
    {
        var content = Encoding.UTF8.GetBytes("Vorname;Nachname;E-Mail\nAda;Lovelace;ada@example.test\n");
        var document = CsvDocument.Parse(content).Value;

        var mapping = ImportTemplates.Suggest(ImportTemplates.Detect(document.Headers), document.Headers);

        mapping.Value(document.Rows[0], ImportField.LastName).Should().Be("Lovelace");
    }
}
