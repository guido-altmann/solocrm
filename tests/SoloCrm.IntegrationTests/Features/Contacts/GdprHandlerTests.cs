using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Activities;
using SoloCrm.Application.Features.Common;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Application.Features.Opportunities;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Application.Features.Tags;
using SoloCrm.Application.Features.Tasks;
using SoloCrm.Application.Features.Timeline;
using SoloCrm.Domain.Activities;
using SoloCrm.Domain.Auditing;

namespace SoloCrm.IntegrationTests.Features.Contacts;

/// <summary>GDPR access (US-19) and erasure (US-20) of a contact.</summary>
public sealed class GdprHandlerTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    private const string Body = "Telefonat mit Ada über ihre Kündigung";

    [Fact]
    public async Task Export_ContactWithHistory_ContainsMasterDataActivitiesTasksOpportunitiesAndAudit()
    {
        var data = await ArrangeContactWithHistoryAsync();
        await SendAsync<CreateContact.Command, CreateContact.Result>(new CreateContact.Command("Grace", "Hopper", "grace@example.test"));

        var result = await QueryAsync<ExportContactData.Query, ExportContactData.Result>(new ExportContactData.Query(data.ContactId));

        var export = result.Value;
        export.FormatVersion.Should().Be(1);
        export.ExportedAt.Should().Be(Time.GetUtcNow());
        export.Contact.Email.Should().Be("ada@example.test");
        export.Contact.Address.City.Should().Be("London");
        export.Contact.Organization!.Name.Should().Be("Analytical Engines");
        export.Contact.Tags.Should().Equal("VIP");
        export.Activities.Select(a => a.Body).Should().BeEquivalentTo(Body, "Notiz zur Anfrage");
        export.Tasks.Select(t => t.Title).Should().Equal("Ada zurückrufen");
        export.Opportunities.Should().ContainSingle()
            .Which.Should().Match<ExportContactData.OpportunityData>(o => o.Title == "Migration" && o.Role == ExportContactData.ContactRole.PrimaryContact);

        // Contact (created, tag), both activities (created) and the deleted one (created, deleted), task (created).
        export.AuditEntries.Should().OnlyContain(a => a.EntityId == data.ContactId
            || data.ActivityIds.Contains(a.EntityId) || a.EntityId == data.DeletedActivityId || a.EntityId == data.TaskId);
        export.AuditEntries.Should().Contain(a => a.EntityId == data.DeletedActivityId && a.Action == AuditAction.Deleted);
        export.AuditEntries.Should().Contain(a => a.EntityId == data.ContactId && a.Changes.Any(c => c.Field == "Tags"));
        export.AuditEntries.Should().NotContain(a => a.Changes.Any(c => c.New == "grace@example.test"));
    }

    [Fact]
    public async Task Export_ArchivedContact_IsExported()
    {
        var id = (await SendAsync<CreateContact.Command, CreateContact.Result>(new CreateContact.Command("Ada", "Lovelace"))).Value.Id;
        await SendAsync<ArchiveContact.Command, ArchiveContact.Result>(new ArchiveContact.Command(id));

        var result = await QueryAsync<ExportContactData.Query, ExportContactData.Result>(new ExportContactData.Query(id));

        result.Value.Contact.IsArchived.Should().BeTrue();
    }

    [Fact]
    public async Task Export_UnknownContact_ReturnsNotFound()
    {
        var result = await QueryAsync<ExportContactData.Query, ExportContactData.Result>(new ExportContactData.Query(Guid.CreateVersion7()));

        result.Error.Should().Be(ContactErrors.NotFound);
    }

    [Fact]
    public async Task ToJson_Export_UsesCamelCaseUtcTimestampsAndEnumNames()
    {
        var data = await ArrangeContactWithHistoryAsync();
        var export = (await QueryAsync<ExportContactData.Query, ExportContactData.Result>(new ExportContactData.Query(data.ContactId))).Value;

        using var json = JsonDocument.Parse(ExportContactData.ToJson(export));

        var root = json.RootElement;
        root.GetProperty("formatVersion").GetInt32().Should().Be(1);
        root.GetProperty("exportedAt").GetString().Should().Be("2026-09-29T08:01:00.000Z");
        root.GetProperty("contact").GetProperty("organization").GetProperty("type").GetString().Should().Be("other");
        root.GetProperty("activities")[0].GetProperty("type").GetString().Should().Be("call");
        root.GetProperty("auditEntries")[0].GetProperty("changes")[0].TryGetProperty("field", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Erasure_ContactWithHistory_ReportsWhatIsAffected()
    {
        var data = await ArrangeContactWithHistoryAsync();

        var result = await QueryAsync<GetContactErasure.Query, GetContactErasure.Result>(new GetContactErasure.Query(data.ContactId));

        result.Value.Should().BeEquivalentTo(new GetContactErasure.Result(
            data.ContactId, "Ada Lovelace", 2, 1, [new RecordRef(data.OpportunityId, "Migration")]));
    }

    [Fact]
    public async Task Delete_ConfirmedByName_RemovesContactActivitiesTasksAndTagsButKeepsOpportunity()
    {
        var data = await ArrangeContactWithHistoryAsync();

        var result = await SendAsync<DeleteContactPermanently.Command, DeleteContactPermanently.Result>(
            new DeleteContactPermanently.Command(data.ContactId, " Ada  Lovelace "));

        result.Value.Should().Be(new DeleteContactPermanently.Result(data.ContactId, 2, 1, 1));
        await using var db = OpenDb();
        (await db.Contacts.AnyAsync(Ct)).Should().BeFalse();
        (await db.Activities.AnyAsync(Ct)).Should().BeFalse();
        (await db.Tasks.AnyAsync(Ct)).Should().BeFalse();
        (await db.ContactTags.AnyAsync(Ct)).Should().BeFalse();
        var opportunity = await db.Opportunities.SingleAsync(Ct);
        opportunity.PrimaryContactId.Should().BeNull();
        (await db.Organizations.CountAsync(Ct)).Should().Be(1);
    }

    [Fact]
    public async Task Delete_ConfirmedByName_LeavesNoFieldValueOfTheContactInTheAudit()
    {
        var data = await ArrangeContactWithHistoryAsync();

        await SendAsync<DeleteContactPermanently.Command, DeleteContactPermanently.Result>(
            new DeleteContactPermanently.Command(data.ContactId, "Ada Lovelace"));

        await using var db = OpenDb();
        var table = string.Join('\n', await db.Database
            .SqlQuery<string>($"SELECT a::text AS \"Value\" FROM audit_entries a")
            .ToListAsync(Ct));
        table.Should().NotContain("Lovelace").And.NotContain("ada@example.test").And.NotContain("Kündigung")
            .And.NotContain("Notiz zur Anfrage").And.NotContain("zurückrufen").And.NotContain("London");

        var deleted = await db.AuditEntries.SingleAsync(a => a.EntityId == data.ContactId && a.Action == AuditAction.Deleted, Ct);
        deleted.Changes.Should().BeEmpty();

        // The tag itself is not personal data and stays; only its assignment to the contact is gone.
        (await db.Tags.SingleAsync(Ct)).Name.Should().Be("VIP");
        (await db.AuditEntries.Where(a => a.EntityId == data.ContactId).ToListAsync(Ct)).Should().OnlyContain(a => a.Changes.Count == 0);

        // Entries of other records keep the bare id (decision 3).
        var opportunityCreated = await db.AuditEntries.SingleAsync(a => a.EntityId == data.OpportunityId && a.Action == AuditAction.Created, Ct);
        opportunityCreated.Changes.Should().Contain(c => c.Field == "PrimaryContactId" && c.New == data.ContactId.ToString());
    }

    [Fact]
    public async Task Delete_ConfirmedByName_WritesContactDeletedToTheOutbox()
    {
        var data = await ArrangeContactWithHistoryAsync();

        await SendAsync<DeleteContactPermanently.Command, DeleteContactPermanently.Result>(
            new DeleteContactPermanently.Command(data.ContactId, "Ada Lovelace"));

        await using var db = OpenDb();
        var message = await db.OutboxMessages.SingleAsync(m => m.Type == "contact.deleted", Ct);
        using var payload = JsonDocument.Parse(message.Payload);
        payload.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("contactId");
        payload.RootElement.GetProperty("contactId").GetGuid().Should().Be(data.ContactId);
    }

    [Fact]
    public async Task Delete_ConfirmedByName_LeavesNoContentInTheTimelinesOfOrganizationAndOpportunity()
    {
        var data = await ArrangeContactWithHistoryAsync();

        await SendAsync<DeleteContactPermanently.Command, DeleteContactPermanently.Result>(
            new DeleteContactPermanently.Command(data.ContactId, "Ada Lovelace"));

        foreach (var (type, id) in new[] { (TimelineRecordType.Organization, data.OrganizationId), (TimelineRecordType.Opportunity, data.OpportunityId) })
        {
            var timeline = await QueryAsync<GetTimeline.Query, GetTimeline.Result>(new GetTimeline.Query(type, id));
            timeline.Value.Entries.Should().NotContain(e => e.Kind == TimelineEntryKind.Activity || e.Kind == TimelineEntryKind.TaskCreated);
            timeline.Value.Entries.Should().NotContain(e => e.Via != null && e.Via.Type == TimelineRecordType.Contact);
        }
    }

    [Fact]
    public async Task Delete_WrongName_ReturnsValidationErrorAndKeepsContact()
    {
        var data = await ArrangeContactWithHistoryAsync();

        var result = await SendAsync<DeleteContactPermanently.Command, DeleteContactPermanently.Result>(
            new DeleteContactPermanently.Command(data.ContactId, "ada lovelace"));

        result.Error.Should().Be(ContactErrors.ErasureNotConfirmed);
        await using var db = OpenDb();
        (await db.Contacts.CountAsync(Ct)).Should().Be(1);
        (await db.Activities.CountAsync(Ct)).Should().Be(2);
    }

    [Fact]
    public async Task Delete_EmptyConfirmation_ReturnsValidationError()
    {
        var data = await ArrangeContactWithHistoryAsync();

        var result = await SendAsync<DeleteContactPermanently.Command, DeleteContactPermanently.Result>(
            new DeleteContactPermanently.Command(data.ContactId, "  "));

        result.Error.Should().BeOfType<ValidationError>()
            .Which.Errors.Should().ContainKey(nameof(DeleteContactPermanently.Command.Confirmation));
    }

    [Fact]
    public async Task Delete_UnknownContact_ReturnsNotFound()
    {
        var result = await SendAsync<DeleteContactPermanently.Command, DeleteContactPermanently.Result>(
            new DeleteContactPermanently.Command(Guid.CreateVersion7(), "Ada Lovelace"));

        result.Error.Should().Be(ContactErrors.NotFound);
    }

    /// <summary>
    /// Ada at Analytical Engines with tag „VIP“, a call, a note on her opportunity „Migration“, a task and an
    /// activity that was deleted before.
    /// </summary>
    private async Task<History> ArrangeContactWithHistoryAsync()
    {
        var organizationId = (await SendAsync<CreateOrganization.Command, CreateOrganization.Result>(
            new CreateOrganization.Command("Analytical Engines"))).Value.Id;
        var contactId = (await SendAsync<CreateContact.Command, CreateContact.Result>(new CreateContact.Command(
            "Ada", "Lovelace", "ada@example.test", OrganizationId: organizationId,
            Address: AddressData.Empty with { City = "London" }))).Value.Id;
        await SendAsync<AssignTag.Command, AssignTag.Result>(new AssignTag.Command(TimelineRecordType.Contact, contactId, NewTagName: "VIP"));
        var opportunityId = (await SendAsync<CreateOpportunity.Command, CreateOpportunity.Result>(new CreateOpportunity.Command(
            "Migration", ClientOrganizationId: organizationId, PrimaryContactId: contactId))).Value.Id;

        Time.Advance(TimeSpan.FromMinutes(1));
        var call = await LogAsync(new LogActivity.Command(ActivityType.Call, Body, ContactId: contactId));
        var note = await LogAsync(new LogActivity.Command(ActivityType.Note, "Notiz zur Anfrage", ContactId: contactId, OpportunityId: opportunityId));
        var deleted = await LogAsync(new LogActivity.Command(ActivityType.Note, "Später gelöscht: Lovelace", ContactId: contactId, OrganizationId: organizationId));
        await SendAsync<DeleteActivity.Command, DeleteActivity.Result>(new DeleteActivity.Command(deleted));
        var taskId = (await SendAsync<CreateTask.Command, CreateTask.Result>(new CreateTask.Command("Ada zurückrufen", ContactId: contactId))).Value.Id;

        return new History(contactId, organizationId, opportunityId, [call, note], deleted, taskId);
    }

    private async Task<Guid> LogAsync(LogActivity.Command command) =>
        (await SendAsync<LogActivity.Command, LogActivity.Result>(command)).Value.Id;

    private sealed record History(
        Guid ContactId, Guid OrganizationId, Guid OpportunityId, IReadOnlyList<Guid> ActivityIds, Guid DeletedActivityId, Guid TaskId);
}
