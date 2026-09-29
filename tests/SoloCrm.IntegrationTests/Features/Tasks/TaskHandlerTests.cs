using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Application.Features.Opportunities;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Application.Features.Tasks;
using SoloCrm.Application.Features.Timeline;
using SoloCrm.Domain.Auditing;
using SoloCrm.Domain.Tasks;

namespace SoloCrm.IntegrationTests.Features.Tasks;

public sealed class TaskHandlerTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(Start.Date);

    [Fact]
    public async Task Create_LinkedAndFree_PersistsTasks()
    {
        var contactId = await CreateContactAsync();

        var linked = await CreateAsync(new CreateTask.Command(" Nachfassen ", Today, ContactId: contactId));
        var free = await CreateAsync(new CreateTask.Command("Steuer"));

        await using var db = OpenDb();
        (await db.Tasks.SingleAsync(t => t.Id == linked, Ct)).Should().Match<TaskItem>(t =>
            t.Title == "Nachfassen" && t.DueDate == Today && t.ContactId == contactId && t.CompletedAt == null);
        (await db.Tasks.SingleAsync(t => t.Id == free, Ct)).ContactId.Should().BeNull();
    }

    [Fact]
    public async Task Create_InvalidOrUnknownLink_ReturnsErrors()
    {
        (await SendAsync<CreateTask.Command, CreateTask.Result>(new CreateTask.Command(" ")))
            .Error.Should().BeOfType<ValidationError>();
        (await SendAsync<CreateTask.Command, CreateTask.Result>(new CreateTask.Command("x", OpportunityId: Guid.CreateVersion7())))
            .Error.Should().Be(LinkedRecordErrors.OpportunityNotFound);
    }

    [Fact]
    public async Task Update_ExistingTask_ChangesTitleAndDueDate()
    {
        var id = await CreateAsync(new CreateTask.Command("Alt", Today));

        var result = await SendAsync<UpdateTask.Command, UpdateTask.Result>(new UpdateTask.Command(id, "Neu", null));

        result.IsSuccess.Should().BeTrue();
        await using var db = OpenDb();
        (await db.Tasks.SingleAsync(Ct)).Should().Match<TaskItem>(t => t.Title == "Neu" && t.DueDate == null);
        (await SendAsync<UpdateTask.Command, UpdateTask.Result>(new UpdateTask.Command(Guid.CreateVersion7(), "x", null)))
            .Error.Should().Be(TaskErrors.NotFound);
        (await SendAsync<UpdateTask.Command, UpdateTask.Result>(new UpdateTask.Command(id, "", null)))
            .Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public async Task CompleteThenReopen_WritesBothEventsAndClearsCompletedAt()
    {
        var id = await CreateAsync(new CreateTask.Command("Nachfassen"));

        var completed = await SendAsync<CompleteTask.Command, CompleteTask.Result>(new CompleteTask.Command(id));
        completed.Value.Should().Be(new CompleteTask.Result(id, Start));
        await using (var db = OpenDb())
        {
            (await db.Tasks.SingleAsync(Ct)).CompletedAt.Should().Be(Start);
        }

        var reopened = await SendAsync<ReopenTask.Command, ReopenTask.Result>(new ReopenTask.Command(id));

        reopened.IsSuccess.Should().BeTrue();
        await using var verify = OpenDb();
        (await verify.Tasks.SingleAsync(Ct)).CompletedAt.Should().BeNull();
        var messages = await verify.OutboxMessages.OrderBy(m => m.Id).Select(m => new { m.Type, m.Payload }).ToListAsync(Ct);
        messages.Select(m => m.Type).Should().Equal("task.completed", "task.reopened");
        messages[0].Payload.Should().Be($$"""{"taskId": "{{id}}", "completedAt": "2026-09-29T08:00:00+00:00"}""");
    }

    [Fact]
    public async Task CompleteReopenDelete_UnknownTask_ReturnNotFound()
    {
        var unknown = Guid.CreateVersion7();

        (await SendAsync<CompleteTask.Command, CompleteTask.Result>(new CompleteTask.Command(unknown))).Error.Should().Be(TaskErrors.NotFound);
        (await SendAsync<ReopenTask.Command, ReopenTask.Result>(new ReopenTask.Command(unknown))).Error.Should().Be(TaskErrors.NotFound);
        (await SendAsync<DeleteTask.Command, DeleteTask.Result>(new DeleteTask.Command(unknown))).Error.Should().Be(TaskErrors.NotFound);
    }

    [Fact]
    public async Task Delete_ExistingTask_RemovesItAndAuditsDeleted()
    {
        var id = await CreateAsync(new CreateTask.Command("Versehentlich"));

        (await SendAsync<DeleteTask.Command, DeleteTask.Result>(new DeleteTask.Command(id))).IsSuccess.Should().BeTrue();

        await using var db = OpenDb();
        (await db.Tasks.AnyAsync(Ct)).Should().BeFalse();
        (await db.AuditEntries.AnyAsync(a => a.EntityId == id && a.Action == AuditAction.Deleted, Ct)).Should().BeTrue();
    }

    [Fact]
    public async Task GetTasks_ForRecord_ReturnsOpenDirectTasksByDueDateWithNames()
    {
        var contactId = await CreateContactAsync();
        var organizationId = (await SendAsync<CreateOrganization.Command, CreateOrganization.Result>(new CreateOrganization.Command("Contoso"))).Value.Id;
        var later = await CreateAsync(new CreateTask.Command("Später", Today.AddDays(3), contactId, organizationId));
        var noDate = await CreateAsync(new CreateTask.Command("Irgendwann", ContactId: contactId));
        var overdue = await CreateAsync(new CreateTask.Command("Überfällig", Today.AddDays(-1), ContactId: contactId));
        var done = await CreateAsync(new CreateTask.Command("Erledigt", ContactId: contactId));
        await SendAsync<CompleteTask.Command, CompleteTask.Result>(new CompleteTask.Command(done));
        await CreateAsync(new CreateTask.Command("Andere", Today));

        var result = await QueryAsync<GetTasks.Query, GetTasks.Result>(new GetTasks.Query(ContactId: contactId));

        result.Value.Items.Select(t => t.Id).Should().Equal(overdue, later, noDate);
        result.Value.Items[1].Should().Be(new TaskSummary(
            later, "Später", Today.AddDays(3), null, new RecordRef(contactId, "Max Mustermann"), new RecordRef(organizationId, "Contoso"), null));
    }

    [Fact]
    public async Task SearchOpportunities_ByTitle_ReturnsActiveMatchesOpenFirst()
    {
        var open = (await SendAsync<CreateOpportunity.Command, CreateOpportunity.Result>(new CreateOpportunity.Command("Migration Azure"))).Value.Id;
        var archived = (await SendAsync<CreateOpportunity.Command, CreateOpportunity.Result>(new CreateOpportunity.Command("Migration alt"))).Value.Id;
        await SendAsync<ArchiveOpportunity.Command, ArchiveOpportunity.Result>(new ArchiveOpportunity.Command(archived));
        await SendAsync<CreateOpportunity.Command, CreateOpportunity.Result>(new CreateOpportunity.Command("Architektur"));

        var result = await QueryAsync<SearchOpportunities.Query, SearchOpportunities.Result>(new SearchOpportunities.Query("migr"));

        result.Value.Items.Select(i => i.Id).Should().Equal(open);
        (await QueryAsync<SearchOpportunities.Query, SearchOpportunities.Result>(new SearchOpportunities.Query(null, 0)))
            .Error.Should().BeOfType<ValidationError>();
    }

    private async Task<Guid> CreateAsync(CreateTask.Command command)
    {
        var result = await SendAsync<CreateTask.Command, CreateTask.Result>(command);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        return result.Value.Id;
    }

    private async Task<Guid> CreateContactAsync() =>
        (await SendAsync<CreateContact.Command, CreateContact.Result>(new CreateContact.Command("Max", "Mustermann"))).Value.Id;
}
