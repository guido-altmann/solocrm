using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Activities;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Application.Features.Opportunities;
using SoloCrm.Application.Features.Timeline;
using SoloCrm.Domain.Activities;
using SoloCrm.Domain.Auditing;

namespace SoloCrm.IntegrationTests.Features.Activities;

public sealed class ActivityHandlerTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    [Fact]
    public async Task Log_WithoutOccurredAt_UsesNowAndWritesOutboxEvent()
    {
        var contactId = await CreateContactAsync();

        var result = await LogAsync(new LogActivity.Command(ActivityType.Call, "Budget geklärt", "Erstgespräch", ContactId: contactId));

        await using var db = OpenDb();
        var activity = await db.Activities.SingleAsync(a => a.Id == result.Value.Id, Ct);
        activity.Should().Match<Activity>(a =>
            a.Type == ActivityType.Call && a.OccurredAt == Start && a.Subject == "Erstgespräch" && a.ContactId == contactId);
        var message = await db.OutboxMessages.SingleAsync(m => m.Type == "activity.logged", Ct);
        message.Payload.Should().Contain(result.Value.Id.ToString()).And.NotContain("Budget");
        (await db.AuditEntries.CountAsync(a => a.EntityType == nameof(Activity) && a.Action == AuditAction.Created, Ct)).Should().Be(1);
    }

    [Fact]
    public async Task Log_Backdated_KeepsOccurredAt()
    {
        var opportunityId = (await SendAsync<CreateOpportunity.Command, CreateOpportunity.Result>(new CreateOpportunity.Command("Migration"))).Value.Id;
        var backdated = Start.AddDays(-2);

        var result = await LogAsync(new LogActivity.Command(ActivityType.ApplicationSent, "Profil geschickt", OccurredAt: backdated, OpportunityId: opportunityId));

        await using var db = OpenDb();
        (await db.Activities.SingleAsync(a => a.Id == result.Value.Id, Ct)).OccurredAt.Should().Be(backdated);
    }

    [Fact]
    public async Task Log_UnknownLinkedRecord_ReturnsNotFound()
    {
        (await LogAsync(new LogActivity.Command(ActivityType.Note, "x", ContactId: Guid.CreateVersion7())))
            .Error.Should().Be(LinkedRecordErrors.ContactNotFound);
        (await LogAsync(new LogActivity.Command(ActivityType.Note, "x", OrganizationId: Guid.CreateVersion7())))
            .Error.Should().Be(LinkedRecordErrors.OrganizationNotFound);
        (await LogAsync(new LogActivity.Command(ActivityType.Note, "x", OpportunityId: Guid.CreateVersion7())))
            .Error.Should().Be(LinkedRecordErrors.OpportunityNotFound);
    }

    [Fact]
    public async Task Log_Invalid_ReturnsValidationError()
    {
        (await LogAsync(new LogActivity.Command(ActivityType.Note, "")))
            .Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public async Task Update_ExistingActivity_ChangesContentAndAudits()
    {
        var contactId = await CreateContactAsync();
        var id = (await LogAsync(new LogActivity.Command(ActivityType.Note, "Alt", ContactId: contactId))).Value.Id;

        var result = await SendAsync<UpdateActivity.Command, UpdateActivity.Result>(
            new UpdateActivity.Command(id, ActivityType.Meeting, Start.AddHours(-1), "Termin", "Neu"));

        result.IsSuccess.Should().BeTrue();
        await using var db = OpenDb();
        (await db.Activities.SingleAsync(Ct)).Should().Match<Activity>(a =>
            a.Type == ActivityType.Meeting && a.Body == "Neu" && a.Subject == "Termin" && a.ContactId == contactId);
        (await db.AuditEntries.SingleAsync(a => a.EntityId == id && a.Action == AuditAction.Updated, Ct)).Changes
            .Select(c => c.Field).Should().BeEquivalentTo("Type", "OccurredAt", "Subject", "Body");
    }

    [Fact]
    public async Task UpdateAndDelete_UnknownOrInvalid_ReturnErrors()
    {
        (await SendAsync<UpdateActivity.Command, UpdateActivity.Result>(
                new UpdateActivity.Command(Guid.CreateVersion7(), ActivityType.Note, Start, null, "x")))
            .Error.Should().Be(ActivityErrors.NotFound);
        (await SendAsync<UpdateActivity.Command, UpdateActivity.Result>(
                new UpdateActivity.Command(Guid.CreateVersion7(), ActivityType.Note, null, null, "x")))
            .Error.Should().BeOfType<ValidationError>();
        (await SendAsync<DeleteActivity.Command, DeleteActivity.Result>(new DeleteActivity.Command(Guid.CreateVersion7())))
            .Error.Should().Be(ActivityErrors.NotFound);
    }

    [Fact]
    public async Task Delete_ExistingActivity_RemovesItAndAuditsDeleted()
    {
        var contactId = await CreateContactAsync();
        var id = (await LogAsync(new LogActivity.Command(ActivityType.Note, "Weg damit", ContactId: contactId))).Value.Id;

        var result = await SendAsync<DeleteActivity.Command, DeleteActivity.Result>(new DeleteActivity.Command(id));

        result.IsSuccess.Should().BeTrue();
        await using var db = OpenDb();
        (await db.Activities.AnyAsync(Ct)).Should().BeFalse();
        (await db.AuditEntries.SingleAsync(a => a.EntityId == id && a.Action == AuditAction.Deleted, Ct)).Changes
            .Should().Contain(new AuditChange("Body", "Weg damit", null));
    }

    private Task<Result<LogActivity.Result>> LogAsync(LogActivity.Command command) =>
        SendAsync<LogActivity.Command, LogActivity.Result>(command);

    private async Task<Guid> CreateContactAsync() =>
        (await SendAsync<CreateContact.Command, CreateContact.Result>(new CreateContact.Command("Max", "Mustermann"))).Value.Id;
}
