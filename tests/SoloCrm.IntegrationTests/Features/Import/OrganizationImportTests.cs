using System.Text;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Application.Features.Import;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Domain.Auditing;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.IntegrationTests.Features.Import;

/// <summary>Organization import (US-16 AK5, decision 15) and linking contacts by HubSpot company id (decision 16).</summary>
public sealed class OrganizationImportTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    private const string CompanyHeader =
        "\"Record ID\",\"Company name\",\"Company Domain Name\",\"Website URL\",\"Type\",\"Street Address\",\"Street Address 2\","
        + "\"Postal Code\",\"City\",\"State/Region\",\"Country/Region\",\"Country/Region Code\",\"Lifecycle Stage\"";

    private const string ContactHeader =
        "\"Record ID\",\"First Name\",\"Last Name\",\"Email\",\"Company Name\",\"Associated Company IDs\"";

    [Fact]
    public async Task Import_HubSpotCompanies_CreatesOrganizationsWithTypeAddressTagAndRecordId()
    {
        var file = Csv(CompanyHeader,
        [
            Company("9001", "Contoso GmbH", domain: "contoso.de", type: "Prospect", street: "Hauptstr. 1", street2: "3. OG",
                postalCode: "10115", city: "Berlin", region: "Berlin", country: "Germany", countryCode: "DE", stage: "Kunde"),
            Company("9002", "Fabrikam AG", website: "https://www.fabrikam.ch", type: "RESELLER", city: "Zürich", country: "Switzerland"),
            Company("9003", "Northwind", type: "Vendor"),
        ]);

        var result = await ImportAsync(file, DuplicateHandling.Skip);

        result.Value.Report.Created.Should().Be(3);
        result.Value.Report.Errors.Should().BeEmpty();
        await using var db = OpenDb();
        var organizations = await db.Organizations.OrderBy(o => o.Name).ToListAsync(Ct);
        var contoso = organizations[0];
        contoso.Type.Should().Be(OrganizationType.Client);
        contoso.Website.Should().Be("https://contoso.de", "the domain is the fallback for the website");
        contoso.Address.Should().Be(Address.Create("Hauptstr. 1", "3. OG", "10115", "Berlin", "Berlin", "DE"));
        contoso.ExtraFields.Should().Equal(new Dictionary<string, string> { ["HubSpotRecordId"] = "9001" });
        organizations[1].Type.Should().Be(OrganizationType.Agency);
        organizations[1].Address.CountryCode.Should().Be("CH", "the country name is the fallback for the code");
        organizations[2].Type.Should().Be(OrganizationType.Other);
        (await db.OrganizationTags.CountAsync(Ct)).Should().Be(1);
        (await db.OutboxMessages.CountAsync(m => m.Type == "organization.created", Ct)).Should().Be(3);
        (await db.AuditEntries.CountAsync(a => a.EntityType == "Organization" && a.Action == AuditAction.Created, Ct)).Should().Be(3);
    }

    [Fact]
    public async Task Import_SameFileTwice_SkipsByRecordIdAndExistingNamesByName()
    {
        await SendAsync<CreateOrganization.Command, CreateOrganization.Result>(new CreateOrganization.Command("NORTHWIND"));
        var file = Csv(CompanyHeader, [Company("9001", "Contoso GmbH"), Company("9003", "Northwind")]);
        await ImportAsync(file, DuplicateHandling.Skip);

        var second = await ImportAsync(Csv(CompanyHeader, [Company("9001", "Contoso (umbenannt)")]), DuplicateHandling.Skip);

        second.Value.Report.Skips.Select(s => s.Reason).Should().Equal("Organisation existiert bereits (HubSpot-ID).");
        await using var db = OpenDb();
        (await db.Organizations.CountAsync(Ct)).Should().Be(2, "Northwind matched the existing organization by name");
    }

    [Fact]
    public async Task Import_Update_OverwritesNonEmptyValuesAndKeepsTheRest()
    {
        var file = Csv(CompanyHeader, [Company("9001", "Contoso GmbH", type: "Prospect", street: "Hauptstr. 1", postalCode: "10115", city: "Berlin", countryCode: "DE")]);
        await ImportAsync(file, DuplicateHandling.Skip);

        var result = await ImportAsync(Csv(CompanyHeader, [Company("9001", "Contoso AG", postalCode: "14467", city: "Potsdam")]), DuplicateHandling.Update);

        result.Value.Report.Updated.Should().Be(1);
        await using var db = OpenDb();
        var organization = await db.Organizations.SingleAsync(Ct);
        organization.Name.Should().Be("Contoso AG");
        organization.Type.Should().Be(OrganizationType.Client, "an empty type keeps the existing one");
        organization.Address.Should().Be(Address.Create("Hauptstr. 1", null, "14467", "Potsdam", null, "DE"));
    }

    [Fact]
    public async Task Import_InvalidRows_ReportsRowAndReason()
    {
        var file = Csv(CompanyHeader,
        [
            Company("1", ""),
            Company("2", "Contoso", website: "kein url mit leerzeichen"),
            Company("3", "Fabrikam", country: "Atlantis"),
            Company("4", "Northwind"),
            Company("5", "northwind"),
        ]);

        var result = await ImportAsync(file, DuplicateHandling.Skip);

        result.Value.Report.Created.Should().Be(1);
        result.Value.Report.Errors.Should().Equal(
            new ImportRowIssue(2, "Bitte einen Namen angeben."),
            new ImportRowIssue(3, "Bitte eine gültige Website angeben (z. B. example.com)."),
            new ImportRowIssue(4, "Unbekanntes Land „Atlantis“."));
        result.Value.Report.Skips.Should().Equal(new ImportRowIssue(6, "Doppelt in der Datei (wie Zeile 5)."));
    }

    [Fact]
    public async Task Import_MappingForOtherTargetOrWithoutName_ReturnsValidationError()
    {
        var file = Csv(CompanyHeader, [Company("9001", "Contoso")]);

        var contactMapping = await SendAsync<ImportOrganizations.Command, ImportOrganizations.Result>(new ImportOrganizations.Command(
            file, new ImportMapping([new FieldMapping(ImportField.Organization, 1)], Target: ImportTarget.Contacts), DuplicateHandling.Skip));
        var withoutName = await SendAsync<ImportOrganizations.Command, ImportOrganizations.Result>(new ImportOrganizations.Command(
            file, new ImportMapping([new FieldMapping(ImportField.City, 8)], Target: ImportTarget.Organizations), DuplicateHandling.Skip));
        var contactField = await SendAsync<ImportOrganizations.Command, ImportOrganizations.Result>(new ImportOrganizations.Command(
            file,
            new ImportMapping([new FieldMapping(ImportField.Organization, 1), new FieldMapping(ImportField.Email, 2)], Target: ImportTarget.Organizations),
            DuplicateHandling.Skip));

        contactMapping.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Mapping");
        withoutName.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Mapping");
        contactField.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Mapping");
    }

    [Fact]
    public async Task ContactImport_AfterCompanies_LinksByCompanyIdBeforeName()
    {
        await ImportAsync(Csv(CompanyHeader, [Company("9001", "Contoso GmbH"), Company("9002", "Fabrikam AG")]), DuplicateHandling.Skip);
        var contacts = Csv(ContactHeader,
        [
            "\"501\",\"Ada\",\"Lovelace\",\"ada@example.test\",\"Contoso\",\"9001;9002\"",
            "\"502\",\"Grace\",\"Hopper\",\"\",\"Fabrikam AG\",\"\"",
            "\"503\",\"Alan\",\"Turing\",\"\",\"Turing Labs\",\"7777\"",
            "\"504\",\"Linus\",\"Torvalds\",\"\",\"\",\"7777\"",
        ]);

        var result = await ImportAsync(contacts, DuplicateHandling.Skip);

        result.Value.Report.Created.Should().Be(4);
        await using var db = OpenDb();
        var byName = await db.Contacts.Include(c => c.Organization).ToDictionaryAsync(c => c.LastName!, c => c.Organization?.Name, Ct);
        byName["Lovelace"].Should().Be("Contoso GmbH", "the company id wins over the differing company name; the first id is the primary");
        byName["Hopper"].Should().Be("Fabrikam AG", "without company id the name is used");
        byName["Turing"].Should().Be("Turing Labs", "an unknown company id falls back to the name");
        byName["Torvalds"].Should().BeNull("an unknown company id without name links nothing");
        (await db.Organizations.CountAsync(Ct)).Should().Be(3);
    }

    [Fact]
    public async Task ContactImport_UpdateAfterCompanies_LinksExistingContacts()
    {
        var contacts = Csv(ContactHeader, ["\"501\",\"Ada\",\"Lovelace\",\"ada@example.test\",\"\",\"9001\""]);
        await ImportAsync(contacts, DuplicateHandling.Skip);
        await ImportAsync(Csv(CompanyHeader, [Company("9001", "Contoso GmbH")]), DuplicateHandling.Skip);

        var result = await ImportAsync(contacts, DuplicateHandling.Update);

        result.Value.Report.Updated.Should().Be(1);
        await using var db = OpenDb();
        (await db.Contacts.Include(c => c.Organization).SingleAsync(Ct)).Organization!.Name.Should().Be("Contoso GmbH");
    }

    [Fact]
    public async Task ContactImport_UpdateWithoutCompany_KeepsExistingLink()
    {
        var organization = await SendAsync<CreateOrganization.Command, CreateOrganization.Result>(new CreateOrganization.Command("Contoso GmbH"));
        await SendAsync<CreateContact.Command, CreateContact.Result>(
            new CreateContact.Command("Ada", "Lovelace", Email: "ada@example.test", OrganizationId: organization.Value.Id));

        await ImportAsync(Csv(ContactHeader, ["\"501\",\"Ada\",\"King\",\"ada@example.test\",\"\",\"\""]), DuplicateHandling.Update);

        await using var db = OpenDb();
        (await db.Contacts.SingleAsync(Ct)).OrganizationId.Should().Be(organization.Value.Id);
    }

    /// <summary>Imports with the suggested mapping (target and template detected), as the wizard does by default.</summary>
    private async Task<Result<ImportOrganizations.Result>> ImportAsync(byte[] content, DuplicateHandling duplicates)
    {
        var preview = await QueryAsync<PreviewImport.Query, PreviewImport.Result>(new PreviewImport.Query(content));
        var mapping = preview.Value.SuggestedMapping;
        if (mapping.Target == ImportTarget.Organizations)
        {
            return await SendAsync<ImportOrganizations.Command, ImportOrganizations.Result>(new ImportOrganizations.Command(content, mapping, duplicates));
        }

        var contacts = await SendAsync<ImportContacts.Command, ImportContacts.Result>(new ImportContacts.Command(content, mapping, duplicates));
        return contacts.Match(r => Result<ImportOrganizations.Result>.Success(new ImportOrganizations.Result(r.Report)), Result<ImportOrganizations.Result>.Failure);
    }

    private static byte[] Csv(string header, IEnumerable<string> rows) =>
        Encoding.UTF8.GetBytes(string.Join("\n", [header, .. rows]) + "\n");

    private static string Company(
        string id,
        string name,
        string domain = "",
        string website = "",
        string type = "",
        string street = "",
        string street2 = "",
        string postalCode = "",
        string city = "",
        string region = "",
        string country = "",
        string countryCode = "",
        string stage = "") =>
        string.Join(',', new[] { id, name, domain, website, type, street, street2, postalCode, city, region, country, countryCode, stage }
            .Select(v => $"\"{v}\""));
}
