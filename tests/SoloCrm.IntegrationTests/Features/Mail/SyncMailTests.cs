using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Mail;
using SoloCrm.Domain.Activities;
using SoloCrm.Domain.Contacts;
using SoloCrm.Domain.Mail;

namespace SoloCrm.IntegrationTests.Features.Mail;

public sealed class SyncMailTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    private static readonly DateTimeOffset Received = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeMailSource _source = new();
    private readonly RecordingConnector _connector = new();

    private SyncMail.Handler CreateHandler(MailSyncOptions? options = null) =>
        new(Get<ICrmDbContextFactory>(), _source, _connector, options ?? new MailSyncOptions(), Get<AppClock>());

    private async Task<Contact> AddContactAsync(string email, bool archived = false)
    {
        var contact = Contact.Create("Max", "Mustermann", email);
        if (archived)
        {
            contact.Archive();
        }

        await using var db = OpenDb();
        db.Contacts.Add(contact);
        await db.SaveChangesAsync(Ct);
        return contact;
    }

    private static IncomingMail Mail(
        string id,
        MailFolder folder = MailFolder.Inbox,
        string? sender = "max@example.test",
        string[]? recipients = null,
        string? subject = "Angebot",
        string? body = "Hallo Guido",
        bool html = false,
        string? messageId = null) =>
        new(id, messageId ?? $"<{id}@example.test>", folder, sender, recipients ?? ["guido@example.test"], subject, Received, body, html);

    private static MailBatch Done(string token, params IncomingMail[] mails) => new(mails, null, token);

    [Fact]
    public async Task Handle_IncomingMailFromKnownContact_LogsInboundActivity()
    {
        var contact = await AddContactAsync("max@example.test");
        _source.Respond(MailFolder.Inbox, Done("d1", Mail("m1", subject: "Angebot", body: "<p>Hallo</p><blockquote>Alt</blockquote>", html: true)));

        var result = await CreateHandler().Handle(new SyncMail.Command(), Ct);

        result.Value.Should().Be(new SyncMail.Result(1, 0, 0));
        _connector.Recorded.Should().Equal((string?)null);
        await using var db = OpenDb();
        var activity = await db.Activities.SingleAsync(Ct);
        activity.Type.Should().Be(ActivityType.Email);
        activity.Origin.Should().Be(ActivityOrigin.Mail);
        activity.Direction.Should().Be(ActivityDirection.Inbound);
        activity.ContactId.Should().Be(contact.Id);
        activity.OrganizationId.Should().BeNull();
        activity.Subject.Should().Be("Angebot");
        activity.Body.Should().Be("Hallo");
        activity.OccurredAt.Should().Be(Received);
        var link = await db.MailMessages.SingleAsync(Ct);
        link.ActivityId.Should().Be(activity.Id);
        link.Provider.Should().Be("Fake");
        link.InternetMessageId.Should().Be("<m1@example.test>");
    }

    [Fact]
    public async Task Handle_OutgoingMail_LogsOutboundActivityForFirstKnownRecipient()
    {
        await AddContactAsync("cc@example.test");
        var to = await AddContactAsync("to@example.test");
        _source.Respond(MailFolder.Sent, Done("d1", Mail("s1", MailFolder.Sent, sender: "guido@example.test", recipients: ["unknown@example.test", "to@example.test", "cc@example.test"])));

        var result = await CreateHandler().Handle(new SyncMail.Command(), Ct);

        result.Value.Created.Should().Be(1);
        await using var db = OpenDb();
        var activity = await db.Activities.SingleAsync(Ct);
        activity.Direction.Should().Be(ActivityDirection.Outbound);
        activity.ContactId.Should().Be(to.Id);
    }

    [Fact]
    public async Task Handle_IncomingMailFromUnknownSenderWithKnownRecipient_LogsWithThatContact()
    {
        var known = await AddContactAsync("kollege@example.test");
        _source.Respond(MailFolder.Inbox, Done("d1", Mail("m1", sender: "fremd@example.test", recipients: ["guido@example.test", "kollege@example.test"])));

        var result = await CreateHandler().Handle(new SyncMail.Command(), Ct);

        result.Value.Created.Should().Be(1);
        await using var db = OpenDb();
        (await db.Activities.SingleAsync(Ct)).ContactId.Should().Be(known.Id);
    }

    [Fact]
    public async Task Handle_EmailAddressesDifferInCase_AreMatched()
    {
        await AddContactAsync("Max.Mustermann@Example.Test");
        _source.Respond(MailFolder.Inbox, Done("d1", Mail("m1", sender: "max.mustermann@example.TEST")));

        var result = await CreateHandler().Handle(new SyncMail.Command(), Ct);

        result.Value.Created.Should().Be(1);
    }

    [Fact]
    public async Task Handle_UnknownOrArchivedContacts_StoresNothing()
    {
        await AddContactAsync("archiviert@example.test", archived: true);
        _source.Respond(MailFolder.Inbox, Done("d1", Mail("m1", sender: "fremd@example.test"), Mail("m2", sender: "archiviert@example.test")));

        var result = await CreateHandler().Handle(new SyncMail.Command(), Ct);

        result.Value.Should().Be(new SyncMail.Result(0, 0, 2));
        await using var db = OpenDb();
        (await db.Activities.AnyAsync(Ct)).Should().BeFalse();
        (await db.MailMessages.AnyAsync(Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Handle_SameMessageInInboxAndSent_LogsOnce()
    {
        await AddContactAsync("max@example.test");
        var inbox = Mail("a1", MailFolder.Inbox, messageId: "<same@example.test>");
        var sent = Mail("b1", MailFolder.Sent, sender: "guido@example.test", recipients: ["max@example.test"], messageId: "<same@example.test>");
        _source.Respond(MailFolder.Inbox, Done("d1", inbox));
        _source.Respond(MailFolder.Sent, Done("d2", sent));

        var result = await CreateHandler().Handle(new SyncMail.Command(), Ct);

        result.Value.Should().Be(new SyncMail.Result(1, 1, 0));
        await using var db = OpenDb();
        (await db.Activities.CountAsync(Ct)).Should().Be(1);
    }

    [Fact]
    public async Task Handle_SecondRunWithSameMessages_LogsNoDuplicates()
    {
        await AddContactAsync("max@example.test");
        _source.Respond(MailFolder.Inbox, Done("d1", Mail("m1")));
        await CreateHandler().Handle(new SyncMail.Command(), Ct);
        _source.Respond(MailFolder.Inbox, Done("d2", Mail("m1")));

        var result = await CreateHandler().Handle(new SyncMail.Command(), Ct);

        result.Value.Should().Be(new SyncMail.Result(0, 1, 0));
        await using var db = OpenDb();
        (await db.Activities.CountAsync(Ct)).Should().Be(1);
    }

    [Fact]
    public async Task Handle_FirstSync_LoadsBackfillPeriodAndStoresDeltaToken()
    {
        _source.Respond(MailFolder.Inbox, Done("inbox-token"));
        _source.Respond(MailFolder.Sent, Done("sent-token"));

        var result = await CreateHandler().Handle(new SyncMail.Command(), Ct);

        result.IsSuccess.Should().BeTrue();
        _source.Calls.Should().Equal(
            new FakeMailSource.Call(MailFolder.Inbox, null, Start.AddDays(-30)),
            new FakeMailSource.Call(MailFolder.Sent, null, Start.AddDays(-30)));
        await using var db = OpenDb();
        var states = await db.MailSyncStates.OrderBy(s => s.Folder).ToListAsync(Ct);
        states.Select(s => (s.Folder, s.DeltaToken)).Should().Equal((MailFolder.Inbox, "inbox-token"), (MailFolder.Sent, "sent-token"));
        states.Should().OnlyContain(s => s.LastSyncedAt == Start);
    }

    [Fact]
    public async Task Handle_ConfiguredBackfillDays_AreUsed()
    {
        _source.Respond(MailFolder.Inbox, Done("t1"));
        _source.Respond(MailFolder.Sent, Done("t2"));

        await CreateHandler(new MailSyncOptions { BackfillDays = 0 }).Handle(new SyncMail.Command(), Ct);

        _source.Calls.Should().OnlyContain(c => c.Since == Start);
    }

    [Fact]
    public async Task Handle_FollowingSync_ContinuesFromStoredDeltaToken()
    {
        _source.Respond(MailFolder.Inbox, Done("inbox-1"));
        _source.Respond(MailFolder.Sent, Done("sent-1"));
        await CreateHandler().Handle(new SyncMail.Command(), Ct);
        _source.Calls.Clear();
        _source.Respond(MailFolder.Inbox, Done("inbox-2"));
        _source.Respond(MailFolder.Sent, Done("sent-2"));

        await CreateHandler().Handle(new SyncMail.Command(), Ct);

        _source.Calls.Should().Equal(
            new FakeMailSource.Call(MailFolder.Inbox, "inbox-1", null),
            new FakeMailSource.Call(MailFolder.Sent, "sent-1", null));
        await using var db = OpenDb();
        (await db.MailSyncStates.CountAsync(Ct)).Should().Be(2);
        (await db.MailSyncStates.SingleAsync(s => s.Folder == MailFolder.Inbox, Ct)).DeltaToken.Should().Be("inbox-2");
    }

    [Fact]
    public async Task Handle_MultiplePages_ProcessesAllAndStoresOnlyTheFinalDeltaToken()
    {
        await AddContactAsync("max@example.test");
        _source.Respond(MailFolder.Inbox, new MailBatch([Mail("m1")], "page-2", null));
        _source.Respond(MailFolder.Inbox, new MailBatch([Mail("m2")], null, "final"));
        _source.Respond(MailFolder.Sent, Done("sent"));

        var result = await CreateHandler().Handle(new SyncMail.Command(), Ct);

        result.Value.Created.Should().Be(2);
        _source.Calls.Take(2).Should().Equal(
            new FakeMailSource.Call(MailFolder.Inbox, null, Start.AddDays(-30)),
            new FakeMailSource.Call(MailFolder.Inbox, "page-2", null));
        await using var db = OpenDb();
        (await db.MailSyncStates.SingleAsync(s => s.Folder == MailFolder.Inbox, Ct)).DeltaToken.Should().Be("final");
    }

    [Fact]
    public async Task Handle_ExpiredDeltaToken_RestartsWithBackfill()
    {
        await AddContactAsync("max@example.test");
        _source.Respond(MailFolder.Inbox, Done("old"));
        _source.Respond(MailFolder.Sent, Done("sent-1"));
        await CreateHandler().Handle(new SyncMail.Command(), Ct);
        _source.Calls.Clear();
        _source.Throw(MailFolder.Inbox, new MailDeltaExpiredException());
        _source.Respond(MailFolder.Inbox, Done("new", Mail("m1")));
        _source.Respond(MailFolder.Sent, Done("sent-2"));

        Time.Advance(TimeSpan.FromDays(1));
        var result = await CreateHandler().Handle(new SyncMail.Command(), Ct);

        result.Value.Created.Should().Be(1);
        _source.Calls.Take(2).Should().Equal(
            new FakeMailSource.Call(MailFolder.Inbox, "old", null),
            new FakeMailSource.Call(MailFolder.Inbox, null, Start.AddDays(1).AddDays(-30)));
        await using var db = OpenDb();
        (await db.MailSyncStates.SingleAsync(s => s.Folder == MailFolder.Inbox, Ct)).DeltaToken.Should().Be("new");
    }

    [Fact]
    public async Task Handle_SourceFails_ReturnsErrorAndKeepsProgressAndToken()
    {
        await AddContactAsync("max@example.test");
        _source.Respond(MailFolder.Inbox, new MailBatch([Mail("m1")], "page-2", null));
        _source.Throw(MailFolder.Inbox, new MailSourceException("Anmeldung abgelaufen."));

        var result = await CreateHandler().Handle(new SyncMail.Command(), Ct);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Mail.SourceFailed");
        result.Error.Message.Should().Contain("Anmeldung abgelaufen.");
        _connector.Recorded.Should().ContainSingle().Which.Should().Contain("Anmeldung abgelaufen.");
        await using var db = OpenDb();
        (await db.Activities.CountAsync(Ct)).Should().Be(1);
        (await db.MailSyncStates.AnyAsync(Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Handle_LastPageWithoutDeltaToken_ReturnsError()
    {
        _source.Respond(MailFolder.Inbox, new MailBatch([], null, null));

        var result = await CreateHandler().Handle(new SyncMail.Command(), Ct);

        result.Error.Code.Should().Be("Mail.DeltaTokenMissing");
    }

    [Fact]
    public async Task Handle_DefaultOptions_RaiseNoActivityLoggedEvent()
    {
        await AddContactAsync("max@example.test");
        _source.Respond(MailFolder.Inbox, Done("d1", Mail("m1")));
        _source.Respond(MailFolder.Sent, Done("d2"));

        await CreateHandler().Handle(new SyncMail.Command(), Ct);

        await using var db = OpenDb();
        (await db.OutboxMessages.AnyAsync(m => m.Type == "activity.logged", Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Handle_EventsEnabled_RaiseActivityLogged()
    {
        await AddContactAsync("max@example.test");
        _source.Respond(MailFolder.Inbox, Done("d1", Mail("m1")));
        _source.Respond(MailFolder.Sent, Done("d2"));

        await CreateHandler(new MailSyncOptions { RaiseActivityLoggedEvents = true }).Handle(new SyncMail.Command(), Ct);

        await using var db = OpenDb();
        (await db.OutboxMessages.CountAsync(m => m.Type == "activity.logged", Ct)).Should().Be(1);
    }

    [Fact]
    public async Task Handle_MailWithoutTextOrMessageId_UsesPlaceholdersAndExternalId()
    {
        await AddContactAsync("max@example.test");
        var longSubject = new string('x', 300);
        _source.Respond(MailFolder.Inbox, Done("d1", Mail("m1", subject: longSubject, body: "  ", messageId: " ") with { InternetMessageId = null }));
        _source.Respond(MailFolder.Sent, Done("d2"));

        var result = await CreateHandler().Handle(new SyncMail.Command(), Ct);

        result.Value.Created.Should().Be(1);
        await using var db = OpenDb();
        var activity = await db.Activities.SingleAsync(Ct);
        activity.Body.Should().Be(SyncMail.EmptyBodyText);
        activity.Subject.Should().HaveLength(SyncMail.SubjectMaxLength).And.EndWith("…");
        (await db.MailMessages.SingleAsync(Ct)).InternetMessageId.Should().Be("external:m1");
    }

    [Fact]
    public async Task Handle_NoMailboxConnected_ReturnsClearError()
    {
        var result = await SendAsync<SyncMail.Command, SyncMail.Result>(new SyncMail.Command());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Mail.SourceFailed");
        result.Error.Message.Should().Contain("kein Postfach verbunden");
    }

    private sealed class RecordingConnector : IMailConnector
    {
        public List<string?> Recorded { get; } = [];

        public bool IsConfigured => true;

        public Uri BuildAuthorizationUri(string redirectUri, string state, string codeChallenge) => throw new NotSupportedException();

        public Task<string> ConnectAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<MailConnectionInfo?> GetConnectionAsync(CancellationToken cancellationToken) => Task.FromResult<MailConnectionInfo?>(null);

        public Task RecordSyncAsync(string? error, CancellationToken cancellationToken)
        {
            Recorded.Add(error);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMailSource : IMailSource
    {
        private readonly Dictionary<MailFolder, Queue<object>> _responses = [];

        public string Provider => "Fake";

        public List<Call> Calls { get; } = [];

        public void Respond(MailFolder folder, MailBatch batch) => Queue(folder).Enqueue(batch);

        public void Throw(MailFolder folder, Exception exception) => Queue(folder).Enqueue(exception);

        public Task<MailBatch> GetChangesAsync(MailFolder folder, string? continuation, DateTimeOffset? receivedSince, CancellationToken cancellationToken)
        {
            Calls.Add(new Call(folder, continuation, receivedSince));
            var queue = Queue(folder);
            return (queue.Count > 0 ? queue.Dequeue() : new MailBatch([], null, "empty")) switch
            {
                Exception exception => throw exception,
                MailBatch batch => Task.FromResult(batch),
                _ => throw new InvalidOperationException(),
            };
        }

        private Queue<object> Queue(MailFolder folder) =>
            _responses.TryGetValue(folder, out var queue) ? queue : _responses[folder] = new Queue<object>();

        public sealed record Call(MailFolder Folder, string? Continuation, DateTimeOffset? Since);
    }
}
