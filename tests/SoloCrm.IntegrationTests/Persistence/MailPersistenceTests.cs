using Microsoft.EntityFrameworkCore;
using SoloCrm.Domain.Activities;
using SoloCrm.Domain.Auditing;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Contacts;
using SoloCrm.Domain.Mail;
using SoloCrm.Infrastructure.Persistence;
using SoloCrm.IntegrationTests.Features;

namespace SoloCrm.IntegrationTests.Persistence;

/// <summary>Constraints, delete behavior and audit exclusions of imported emails (ADR-013).</summary>
public sealed class MailPersistenceTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    /// <summary>A context with the production interceptors (audit, timestamps, outbox).</summary>
    private CrmDbContext OpenAuditedDb() => Get<IDbContextFactory<CrmDbContext>>().CreateDbContext();

    private static Activity MailActivity(Guid contactId) =>
        Activity.LogMail(ActivityDirection.Inbound, Start, "Angebot", "Vertraulicher Text", new LinkedRecords(ContactId: contactId));

    [Fact]
    public async Task Save_MailActivity_KeepsSubjectAndBodyOutOfAudit()
    {
        var contact = Contact.Create("Max", "Mustermann");
        var activity = MailActivity(contact.Id);
        await using (var db = OpenAuditedDb())
        {
            db.Contacts.Add(contact);
            db.Activities.Add(activity);
            await db.SaveChangesAsync(Ct);
        }

        await using var verify = OpenDb();
        var entry = await verify.AuditEntries.SingleAsync(a => a.EntityId == activity.Id, Ct);
        entry.Action.Should().Be(AuditAction.Created);
        entry.Changes.Select(c => c.Field).Should().NotContain([nameof(Activity.Subject), nameof(Activity.Body)]);
        entry.Changes.Should().Contain(c => c.Field == nameof(Activity.Direction) && c.New == "Inbound");
    }

    [Fact]
    public async Task Update_MailActivityBody_KeepsBodyOutOfAudit()
    {
        var contact = Contact.Create("Max", "Mustermann");
        var activity = MailActivity(contact.Id);
        await using (var db = OpenAuditedDb())
        {
            db.Contacts.Add(contact);
            db.Activities.Add(activity);
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = OpenAuditedDb())
        {
            var loaded = await db.Activities.SingleAsync(a => a.Id == activity.Id, Ct);
            loaded.Update(ActivityType.Email, Start, "Neuer Betreff", "Neuer Text");
            await db.SaveChangesAsync(Ct);
        }

        await using var verify = OpenDb();
        var entries = await verify.AuditEntries.Where(a => a.EntityId == activity.Id).ToListAsync(Ct);
        entries.SelectMany(e => e.Changes).Select(c => c.Old).Concat(entries.SelectMany(e => e.Changes).Select(c => c.New))
            .Should().NotContain(v => v != null && (v.Contains("Text") || v.Contains("Betreff") || v == "Angebot"));
    }

    [Fact]
    public async Task Save_ManualActivity_StillAuditsBody()
    {
        var contact = Contact.Create("Max", "Mustermann");
        var activity = Activity.Log(ActivityType.Note, Start, null, "Notiz", new LinkedRecords(ContactId: contact.Id));
        await using (var db = OpenAuditedDb())
        {
            db.Contacts.Add(contact);
            db.Activities.Add(activity);
            await db.SaveChangesAsync(Ct);
        }

        await using var verify = OpenDb();
        var entry = await verify.AuditEntries.SingleAsync(a => a.EntityId == activity.Id, Ct);
        entry.Changes.Should().Contain(c => c.Field == nameof(Activity.Body) && c.New == "Notiz");
    }

    [Fact]
    public async Task Insert_ManualActivityWithDirection_ViolatesCheckConstraint()
    {
        var contact = Contact.Create("Max", "Mustermann");
        await using var db = OpenDb();
        db.Contacts.Add(contact);
        await db.SaveChangesAsync(Ct);

        var act = () => db.Database.ExecuteSqlAsync(
            $"INSERT INTO activities (id, type, origin, direction, occurred_at, body, contact_id, created_at, updated_at) VALUES (gen_random_uuid(), 'Email', 'Manual', 'Inbound', now(), 'x', {contact.Id}, now(), now())",
            Ct);

        (await act.Should().ThrowAsync<Npgsql.PostgresException>()).Which.ConstraintName
            .Should().Be("ck_activities_direction_only_for_mail");
    }

    [Fact]
    public async Task Save_SameInternetMessageIdTwice_ViolatesUniqueIndex()
    {
        var contact = Contact.Create("Max", "Mustermann");
        var first = MailActivity(contact.Id);
        var second = MailActivity(contact.Id);
        await using (var db = OpenDb())
        {
            db.Contacts.Add(contact);
            db.Activities.AddRange(first, second);
            db.MailMessages.Add(new MailMessage(first.Id, "Graph", "id-1", "<same@example.test>", MailFolder.Inbox));
            await db.SaveChangesAsync(Ct);
        }

        await using var duplicate = OpenDb();
        duplicate.MailMessages.Add(new MailMessage(second.Id, "Graph", "id-2", "<same@example.test>", MailFolder.Sent));

        var act = () => duplicate.SaveChangesAsync(Ct);

        (await act.Should().ThrowAsync<DbUpdateException>()).WithInnerException<Npgsql.PostgresException>()
            .Which.ConstraintName.Should().Be("ix_mail_messages_provider_internet_message_id");
    }

    [Fact]
    public async Task DeleteContact_WithMailActivity_DeletesActivityAndMailMessage()
    {
        var contact = Contact.Create("Max", "Mustermann");
        var activity = MailActivity(contact.Id);
        await using (var db = OpenDb())
        {
            db.Contacts.Add(contact);
            db.Activities.Add(activity);
            db.MailMessages.Add(new MailMessage(activity.Id, "Graph", "id-1", "<a@example.test>", MailFolder.Inbox));
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = OpenDb())
        {
            await db.Contacts.Where(c => c.Id == contact.Id).ExecuteDeleteAsync(Ct);
        }

        await using var verify = OpenDb();
        (await verify.Activities.AnyAsync(Ct)).Should().BeFalse();
        (await verify.MailMessages.AnyAsync(Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Save_SyncStateForSameFolderTwice_ViolatesUniqueIndex()
    {
        await using (var db = OpenDb())
        {
            db.MailSyncStates.Add(new MailSyncState("Graph", MailFolder.Inbox));
            await db.SaveChangesAsync(Ct);
        }

        await using var duplicate = OpenDb();
        duplicate.MailSyncStates.Add(new MailSyncState("Graph", MailFolder.Inbox));

        var act = () => duplicate.SaveChangesAsync(Ct);

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
