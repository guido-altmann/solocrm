using System.Text;
using SoloCrm.Application.Features.Import;

namespace SoloCrm.Application.Tests.Features.Import;

/// <summary>
/// The header of a real HubSpot company export (2026-10-01, 117 columns; iteration 5 decision 15) against the company template.
/// </summary>
public sealed class HubSpotCompanyExportTests
{
    private const string Header =
        @"""Record ID"",""Updated by user ID"",""Created by user ID"",""Close Date"",""Recent Deal Close Date"",""Intent Signals active"",""Street Address"",""Street Address 2"",""Current Customer"",""Number of Active Contracts"",""Number of blockers"",""Number of decision makers"",""Number of Form Submissions"",""Number of times contacted"",""Number of contacts with a buying role"",""Number of Employees"",""Number of open deals"",""Number of Pageviews"",""Number of Sessions"",""Number of child companies"",""Number of Associated Deals"",""Number of Associated Contacts"",""Description"",""Recent Deal Amount"",""Industry"",""Industry group"",""State/Region Code"",""State/Region"",""Latest Traffic Source Data 1"",""Latest Traffic Source Data 2"",""Record source"",""Record source detail 1"",""Record source detail 2"",""Record source detail 3"",""First Contact Create Date"",""First Conversion Date"",""Last Activity Date"",""Last Modified Date"",""Last Engagement Date"",""Recent Conversion Date"",""Last Logged Outgoing Email Date"",""Owner assigned date"",""Next Activity Date"",""Date of last meeting booked in meetings tool"",""Latest suggested contact date"",""Last Booked Meeting Date"",""Original Traffic Source Drill-Down 1"",""Original Traffic Source Drill-Down 2"",""Domain status"",""Company Domain Name"",""First Conversion"",""Create Date"",""First Deal Created Date"",""Facebook Fans"",""Facebook Company Page"",""Company owner"",""Total open deal value"",""Total Revenue"",""Google Plus Page"",""Year Founded"",""Ideal Customer Profile Tier"",""Total Money Raised"",""Is Public"",""Annual Revenue"",""Campaign of last booking in meetings tool"",""First Touch Converting Campaign"",""Last Touch Converting Campaign"",""Country/Region"",""Country/Region Code"",""Lead Status"",""Recent Intent Signals"",""Last Contacted"",""Recent Conversion"",""Latest Traffic Source"",""Last Open Task Date"",""Last Logged Call Date"",""lh_company_id"",""lh_public_id"",""Lifecycle Stage"",""LinkedIn Bio"",""Linkedin handle"",""LinkedIn Company Page"",""Logo URL"",""Medium of last booking in meetings tool"",""Employee range"",""Next Renewal Date"",""Postal Code"",""Source of last booking in meetings tool"",""Quick context"",""City"",""Days to Close"",""HubSpot Team"",""Phone Number"",""Twitter Bio"",""Twitter Followers"",""Twitter Handle"",""Type"",""About Us"",""Parent Company"",""Revenue range"",""Company Keywords"",""Company name"",""Original Traffic Source"",""Website URL"",""Web Technologies"",""Redirect URL"",""Redirect domain"",""Has been enriched"",""Time First Seen"",""Time of First Session"",""Time Last Seen"",""Time of Last Session"",""Latest Traffic Source Timestamp"",""Latest Suggested Contact Timestamp"",""Time Zone"",""Target Account"",""Merged Company IDs""";

    [Fact]
    public void Detect_RealCompanyExport_ReturnsOrganizationsAndHubSpot()
    {
        var headers = Parse().Headers;

        headers.Should().HaveCount(117);
        ImportTemplates.Detect(headers).Should().Be((ImportTarget.Organizations, ImportTemplate.HubSpot));
    }

    [Fact]
    public void Suggest_RealCompanyExport_MapsEveryTemplateColumn()
    {
        var mapping = ImportTemplates.Suggest(ImportTarget.Organizations, ImportTemplate.HubSpot, Parse().Headers);

        mapping.Fields.Where(f => f.Column is not null).Should().BeEquivalentTo(new FieldMapping[]
        {
            new(ImportField.Organization, 101),
            new(ImportField.OrganizationType, 96),
            new(ImportField.OrganizationWebsite, 103, 49),
            new(ImportField.Street, 6),
            new(ImportField.Street2, 7),
            new(ImportField.PostalCode, 86),
            new(ImportField.City, 89),
            new(ImportField.Region, 27, null),
            new(ImportField.Country, 68, 67),
            new(ImportField.Tag, 78),
            new(ImportField.HubSpotRecordId, 0),
        });
    }

    private static CsvDocument Parse() => CsvDocument.Parse(Encoding.UTF8.GetBytes(Header + "\r\n")).Value;
}
