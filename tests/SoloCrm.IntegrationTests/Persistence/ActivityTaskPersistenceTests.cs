using Microsoft.EntityFrameworkCore;
using SoloCrm.Domain.Activities;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Contacts;
using SoloCrm.Domain.Organizations;
using SoloCrm.Domain.Tasks;
using SoloCrm.IntegrationTests.Features;

namespace SoloCrm.IntegrationTests.Persistence;

/// <summary>
/// Verifies the constraints and delete behaviors of activities and tasks (prepared for GDPR deletion, US-20).
/// </summary>
public sealed class ActivityTaskPersistenceTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    [Fact]
    public async Task Insert_ActivityWithoutLinkedRecord_ViolatesCheckConstraint()
    {
        await using var db = OpenDb();

        var act = () => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO activities (id, type, occurred_at, body, created_at, updated_at) VALUES (gen_random_uuid(), 'Note', now(), 'x', now(), now())",
            Ct);

        (await act.Should().ThrowAsync<Npgsql.PostgresException>()).Which.ConstraintName
            .Should().Be("ck_activities_linked_record_required");
    }

    [Fact]
    public async Task DeleteContact_WithActivitiesAndTasks_DeletesThem()
    {
        var contact = Contact.Create("Max", "Mustermann");
        var organization = Organization.Create("Contoso");
        await using (var db = OpenDb())
        {
            db.AddRange(contact, organization);
            db.Activities.Add(Activity.Log(ActivityType.Call, Start, null, "Anruf", new LinkedRecords(contact.Id, organization.Id)));
            db.Tasks.Add(TaskItem.Create("Nachfassen", linkedTo: new LinkedRecords(ContactId: contact.Id)));
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = OpenDb())
        {
            await db.Contacts.Where(c => c.Id == contact.Id).ExecuteDeleteAsync(Ct);
        }

        await using var verify = OpenDb();
        (await verify.Activities.AnyAsync(Ct)).Should().BeFalse();
        (await verify.Tasks.AnyAsync(Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteOrganization_WithTask_KeepsTaskAsFreeTask()
    {
        var organization = Organization.Create("Contoso");
        var task = TaskItem.Create("Nachfassen", linkedTo: new LinkedRecords(OrganizationId: organization.Id));
        await using (var db = OpenDb())
        {
            db.Add(organization);
            db.Tasks.Add(task);
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = OpenDb())
        {
            await db.Organizations.Where(o => o.Id == organization.Id).ExecuteDeleteAsync(Ct);
        }

        await using var verify = OpenDb();
        (await verify.Tasks.SingleAsync(Ct)).OrganizationId.Should().BeNull();
    }
}
