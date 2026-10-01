using System.Text;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Application.Features.Import;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Application.Features.Tags;
using SoloCrm.Application.Features.Timeline;
using SoloCrm.Domain.Auditing;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Organizations;
using SoloCrm.Domain.Tags;

namespace SoloCrm.IntegrationTests.Features.Import;

/// <summary>CSV import (US-16): preview, mapping, duplicates, report, HubSpot template.</summary>
public sealed class ImportHandlerTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    private const string HubSpotHeader =
        "\"Record ID\",\"First Name\",\"Last Name\",\"Email\",\"Work email\",\"Job Title\",\"LinkedIn URL\",\"Company Name\","
        + "\"Website URL\",\"Original Traffic Source\",\"Lifecycle Stage\",\"Billing Contact IDs\",\"Billing Contact IDs\"";

    [Fact]
    public async Task Preview_HubSpotFile_ShowsFirstTenRowsAndSuggestsHubSpotMapping()
    {
        var rows = Enumerable.Range(1, 12).Select(i => HubSpot($"{i}", $"Vorname{i}", $"Nachname{i}"));

        var result = await PreviewAsync(Csv(HubSpotHeader, rows));

        result.Value.Columns.Should().HaveCount(13);
        result.Value.Rows.Should().HaveCount(PreviewContactImport.PreviewRows);
        result.Value.Rows[0].Number.Should().Be(2);
        result.Value.RowCount.Should().Be(12);
        result.Value.SuggestedMapping.Template.Should().Be(ImportTemplate.HubSpot);
        result.Value.SuggestedMapping.For(ImportField.Email).Should().Be(new FieldMapping(ImportField.Email, 3, 4));
    }

    [Fact]
    public async Task Preview_ExplicitTemplate_OverridesDetection()
    {
        var result = await PreviewAsync(Csv(HubSpotHeader, [HubSpot("1", "Ada", "Lovelace")]), ImportTemplate.Generic);

        result.Value.SuggestedMapping.Template.Should().Be(ImportTemplate.Generic);
    }

    [Fact]
    public async Task Preview_EmptyFile_ReturnsError()
    {
        var result = await PreviewAsync([]);

        result.Error.Should().Be(ImportErrors.EmptyFile);
    }

    [Fact]
    public async Task Import_HubSpotFile_CreatesContactsWithOrganizationSourceTagAndRecordId()
    {
        var existing = await SendAsync<CreateOrganization.Command, CreateOrganization.Result>(new CreateOrganization.Command("Contoso GmbH"));
        await SendAsync<CreateTag.Command, CreateTag.Result>(new CreateTag.Command("Kunde"));
        var file = Csv(HubSpotHeader,
        [
            HubSpot("101", "Ada", "Lovelace", email: "ada@example.test", job: "CTO", linkedIn: "www.linkedin.com/in/ada",
                company: "contoso gmbh", source: "Referrals", stage: "kunde"),
            HubSpot("102", "Grace", "Hopper", workEmail: "grace@navy.test", company: "Navy Labs", website: "navy.test",
                source: "Organic Search", stage: "Lead"),
            HubSpot("103", "Alan", "Turing", company: "Navy Labs", source: "Social Media"),
        ]);

        var result = await ImportAsync(file, DuplicateHandling.Skip);

        result.Value.Created.Should().Be(3);
        result.Value.Errors.Should().BeEmpty();
        await using var db = OpenDb();
        var contacts = await db.Contacts.Include(c => c.Organization).OrderBy(c => c.LastName).ToListAsync(Ct);
        var hopper = contacts[0];
        var lovelace = contacts[1];
        var turing = contacts[2];
        lovelace.OrganizationId.Should().Be(existing.Value.Id);
        lovelace.Source.Should().Be(LeadSource.Referral);
        lovelace.LinkedInUrl.Should().Be("https://www.linkedin.com/in/ada");
        lovelace.ExtraFields.Should().Equal(new Dictionary<string, string> { ["HubSpotRecordId"] = "101" });
        hopper.Email.Should().Be("grace@navy.test", "the work email is the fallback");
        hopper.Source.Should().Be(LeadSource.Website);
        hopper.Organization!.Website.Should().Be("https://navy.test");
        hopper.Organization.Type.Should().Be(OrganizationType.Other);
        turing.OrganizationId.Should().Be(hopper.OrganizationId, "the organization created in the same import is reused");
        turing.Source.Should().BeNull("social media is ignored");
        (await db.Organizations.CountAsync(Ct)).Should().Be(2);
        (await db.Tags.OrderBy(t => t.Name).Select(t => t.Name).ToListAsync(Ct)).Should().Equal("Kunde", "Lead");
        (await db.ContactTags.CountAsync(Ct)).Should().Be(2);
    }

    [Fact]
    public async Task Import_EveryContact_IsAuditedAndRaisesContactCreated()
    {
        var file = Csv("Vorname;Nachname", ["Ada;Lovelace", "Grace;Hopper"]);

        await ImportAsync(file, DuplicateHandling.Skip);

        await using var db = OpenDb();
        (await db.AuditEntries.CountAsync(a => a.EntityType == "Contact" && a.Action == AuditAction.Created, Ct)).Should().Be(2);
        (await db.OutboxMessages.CountAsync(m => m.Type == "contact.created", Ct)).Should().Be(2);
        (await db.Contacts.Select(c => c.CreatedAt).Distinct().SingleAsync(Ct)).Should().Be(Start, "the interceptor sets CreatedAt");
    }

    [Fact]
    public async Task Import_DuplicateEmailWithSkip_SkipsExistingAndReportsRow()
    {
        await SendAsync<CreateContact.Command, CreateContact.Result>(new CreateContact.Command("Ada", "Lovelace", Email: "ada@example.test", JobTitle: "CTO"));
        var file = Csv("Vorname,Nachname,E-Mail,Position", ["Ada,King,ADA@example.test,CEO", "Grace,Hopper,grace@example.test,"]);

        var result = await ImportAsync(file, DuplicateHandling.Skip);

        result.Value.Created.Should().Be(1);
        result.Value.Skips.Should().Equal(new ImportContacts.RowIssue(2, "Kontakt existiert bereits (E-Mail)."));
        await using var db = OpenDb();
        (await db.Contacts.SingleAsync(c => c.Email == "ada@example.test", Ct)).LastName.Should().Be("Lovelace");
    }

    [Fact]
    public async Task Import_DuplicateEmailWithUpdate_OverwritesWithNonEmptyValuesOnly()
    {
        await SendAsync<CreateContact.Command, CreateContact.Result>(
            new CreateContact.Command("Ada", "Lovelace", Email: "ada@example.test", Phone: "+49 30 1", JobTitle: "CTO"));
        var file = Csv("Vorname,Nachname,E-Mail,Telefon,Position,Firma,Tag", ["Ada,King,ada@example.test,,CEO,Analytical Engines,VIP"]);

        var result = await ImportAsync(file, DuplicateHandling.Update);

        result.Value.Updated.Should().Be(1);
        result.Value.Created.Should().Be(0);
        await using var db = OpenDb();
        var contact = await db.Contacts.Include(c => c.Organization).SingleAsync(Ct);
        contact.LastName.Should().Be("King");
        contact.JobTitle.Should().Be("CEO");
        contact.Phone.Should().Be("+49 30 1", "empty cells keep the existing value");
        contact.Organization!.Name.Should().Be("Analytical Engines");
        (await db.ContactTags.CountAsync(Ct)).Should().Be(1);
        (await db.AuditEntries.CountAsync(a => a.EntityType == "Contact" && a.Action == AuditAction.Updated, Ct)).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Import_SameFileTwice_RecognizesContactsWithoutEmailByHubSpotRecordId()
    {
        var file = Csv(HubSpotHeader, [HubSpot("201", "Ada", "Lovelace"), HubSpot("202", "Grace", "Hopper", email: "grace@example.test")]);
        await ImportAsync(file, DuplicateHandling.Skip);

        var second = await ImportAsync(file, DuplicateHandling.Skip);

        second.Value.Created.Should().Be(0);
        second.Value.Skips.Select(s => s.Reason).Should().Equal("Kontakt existiert bereits (HubSpot-ID).", "Kontakt existiert bereits (E-Mail).");
        await using var db = OpenDb();
        (await db.Contacts.CountAsync(Ct)).Should().Be(2);
    }

    [Fact]
    public async Task Import_DuplicatesWithinFile_SkipsLaterRows()
    {
        var file = Csv("Vorname,Nachname,E-Mail", ["Ada,Lovelace,ada@example.test", "Ada,L.,Ada@Example.test", "Grace,Hopper,"]);

        var result = await ImportAsync(file, DuplicateHandling.Update);

        result.Value.Created.Should().Be(2);
        result.Value.Skips.Should().Equal(new ImportContacts.RowIssue(3, "Doppelt in der Datei (wie Zeile 2)."));
    }

    [Fact]
    public async Task Import_InvalidRows_ReportsRowAndReasonAndImportsTheRest()
    {
        var file = Csv("Vorname,Nachname,E-Mail,LinkedIn,Tag",
        [
            "Ada,Lovelace,kein-mail,,",
            ",,,,",
            ",,grace@example.test,,",
            "Alan,Turing,,https://,",
            $"Grace,Hopper,,,{new string('x', 51)}",
            "Linus,Torvalds,linus@example.test,,",
        ]);

        var result = await ImportAsync(file, DuplicateHandling.Skip);

        result.Value.Created.Should().Be(1);
        result.Value.Failed.Should().Be(4);
        result.Value.Errors.Should().Equal(
            new ImportContacts.RowIssue(2, "Bitte eine gültige E-Mail-Adresse angeben."),
            new ImportContacts.RowIssue(4, "Bitte Vor- oder Nachnamen angeben."),
            new ImportContacts.RowIssue(5, "Bitte eine gültige URL (http/https) angeben."),
            new ImportContacts.RowIssue(6, "Der Tag darf höchstens 50 Zeichen lang sein."));
    }

    [Fact]
    public async Task Import_MoreThanOneBlock_ReportsProgressPerBlock()
    {
        var rows = Enumerable.Range(1, 250).Select(i => $"Kontakt,Nummer {i},kontakt{i}@example.test");
        var progress = new List<ImportContacts.Progress>();

        var result = await ImportAsync(Csv("Vorname,Nachname,E-Mail", rows), DuplicateHandling.Skip, new SyncProgress(progress));

        result.Value.Created.Should().Be(250);
        progress.Should().Equal(
            new ImportContacts.Progress(100, 250), new ImportContacts.Progress(200, 250), new ImportContacts.Progress(250, 250));
    }

    [Fact]
    public async Task Import_NewTags_GetPaletteColorsInTurn()
    {
        await SendAsync<CreateTag.Command, CreateTag.Result>(new CreateTag.Command("Bestand"));

        await ImportAsync(Csv("Nachname,Tag", ["Lovelace,Kunde", "Hopper,Partner"]), DuplicateHandling.Skip);

        await using var db = OpenDb();
        (await db.Tags.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).Select(t => t.Color).ToListAsync(Ct))
            .Should().Equal(TagPalette.Colors[0], TagPalette.Colors[1], TagPalette.Colors[2]);
    }

    [Fact]
    public async Task Import_InvalidMapping_ReturnsValidationError()
    {
        var file = Csv("Vorname,Nachname", ["Ada,Lovelace"]);

        var noName = await SendAsync<ImportContacts.Command, ImportContacts.Result>(new ImportContacts.Command(
            file, new ImportMapping([new FieldMapping(ImportField.Email, 0)]), DuplicateHandling.Skip));
        var outOfRange = await SendAsync<ImportContacts.Command, ImportContacts.Result>(new ImportContacts.Command(
            file, new ImportMapping([new FieldMapping(ImportField.LastName, 5)]), DuplicateHandling.Skip));
        var twice = await SendAsync<ImportContacts.Command, ImportContacts.Result>(new ImportContacts.Command(
            file, new ImportMapping([new FieldMapping(ImportField.LastName, 0), new FieldMapping(ImportField.LastName, 1)]), DuplicateHandling.Skip));

        noName.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Mapping");
        outOfRange.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Mapping");
        twice.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Mapping");
    }

    [Fact]
    public async Task Import_ArchivedContactWithSameEmail_IsTreatedAsDuplicate()
    {
        var ada = await SendAsync<CreateContact.Command, CreateContact.Result>(new CreateContact.Command("Ada", "Lovelace", Email: "ada@example.test"));
        await SendAsync<ArchiveContact.Command, ArchiveContact.Result>(new ArchiveContact.Command(ada.Value.Id));

        var result = await ImportAsync(Csv("Nachname,E-Mail", ["King,ada@example.test"]), DuplicateHandling.Skip);

        result.Value.Skipped.Should().Be(1);
        result.Value.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_ExistingTagAssignmentOnUpdate_IsNotDuplicated()
    {
        var ada = await SendAsync<CreateContact.Command, CreateContact.Result>(new CreateContact.Command("Ada", "Lovelace", Email: "ada@example.test"));
        await SendAsync<AssignTag.Command, AssignTag.Result>(new AssignTag.Command(TimelineRecordType.Contact, ada.Value.Id, null, "Kunde"));

        var result = await ImportAsync(Csv("Nachname,E-Mail,Tag", ["Lovelace,ada@example.test,KUNDE"]), DuplicateHandling.Update);

        result.Value.Updated.Should().Be(1);
        await using var db = OpenDb();
        (await db.ContactTags.CountAsync(Ct)).Should().Be(1);
    }

    private Task<Result<PreviewContactImport.Result>> PreviewAsync(byte[] content, ImportTemplate? template = null) =>
        QueryAsync<PreviewContactImport.Query, PreviewContactImport.Result>(new PreviewContactImport.Query(content, template));

    /// <summary>Imports with the suggested mapping, as the wizard does by default.</summary>
    private async Task<Result<ImportContacts.Result>> ImportAsync(
        byte[] content, DuplicateHandling duplicates, IProgress<ImportContacts.Progress>? progress = null)
    {
        var preview = await PreviewAsync(content);
        return await SendAsync<ImportContacts.Command, ImportContacts.Result>(
            new ImportContacts.Command(content, preview.Value.SuggestedMapping, duplicates, progress));
    }

    private static byte[] Csv(string header, IEnumerable<string> rows) =>
        Encoding.UTF8.GetBytes(string.Join("\n", [header, .. rows]) + "\n");

    private static string HubSpot(
        string id,
        string firstName,
        string lastName,
        string email = "",
        string workEmail = "",
        string job = "",
        string linkedIn = "",
        string company = "",
        string website = "",
        string source = "",
        string stage = "") =>
        string.Join(',', new[] { id, firstName, lastName, email, workEmail, job, linkedIn, company, website, source, stage, "1", "2" }
            .Select(v => $"\"{v}\""));

    /// <summary><see cref="Progress{T}"/> posts asynchronously; this one records synchronously.</summary>
    private sealed class SyncProgress(List<ImportContacts.Progress> reports) : IProgress<ImportContacts.Progress>
    {
        public void Report(ImportContacts.Progress value) => reports.Add(value);
    }
}
