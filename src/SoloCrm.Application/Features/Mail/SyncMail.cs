using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Activities;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Mail;

namespace SoloCrm.Application.Features.Mail;

/// <summary>
/// Reads new emails of Inbox and Sent from the mailbox and logs those with known contacts as activities (ADR-013).
/// A mail is logged if sender or a recipient is the email address of a non-archived contact; one activity per mail,
/// linked to the main contact (the sender of an incoming mail, else the first known recipient). Mails are never
/// logged twice (same <c>Message-ID</c>). The first sync of a folder loads <c>Mail:BackfillDays</c> days.
/// </summary>
public static class SyncMail
{
    public const int SubjectMaxLength = Activity.SubjectMaxLength;
    public const int BodyMaxLength = 100_000;
    public const string EmptyBodyText = "(Kein Text)";

    private static readonly MailFolder[] Folders = [MailFolder.Inbox, MailFolder.Sent];

    public sealed record Command;

    /// <param name="Created">Activities created.</param>
    /// <param name="Duplicates">Mails that were already logged.</param>
    /// <param name="Unmatched">Mails without a known contact (not stored).</param>
    public sealed record Result(int Created, int Duplicates, int Unmatched);

    public sealed class Handler(ICrmDbContextFactory dbFactory, IMailSource source, IMailConnector connector, MailSyncOptions options, AppClock clock)
        : ICommandHandler<Command, Result>
    {
        // One sync at a time: the background service and "Jetzt abgleichen" must not import the same mails twice.
        private static readonly SemaphoreSlim Gate = new(1, 1);

        public async Task<Result<Result>> Handle(Command command, CancellationToken cancellationToken)
        {
            await Gate.WaitAsync(cancellationToken);
            try
            {
                var result = await SyncAllAsync(cancellationToken);
                await connector.RecordSyncAsync(result.IsSuccess ? null : result.Error.Message, cancellationToken);
                return result;
            }
            finally
            {
                Gate.Release();
            }
        }

        private async Task<Result<Result>> SyncAllAsync(CancellationToken cancellationToken)
        {
            var total = new Result(0, 0, 0);
            foreach (var folder in Folders)
            {
                var folderResult = await SyncFolderAsync(folder, cancellationToken);
                if (folderResult.IsFailure)
                {
                    return folderResult.Error;
                }

                var r = folderResult.Value;
                total = new Result(total.Created + r.Created, total.Duplicates + r.Duplicates, total.Unmatched + r.Unmatched);
            }

            return total;
        }

        private async Task<Result<Result>> SyncFolderAsync(MailFolder folder, CancellationToken cancellationToken)
        {
            var continuation = await LoadDeltaTokenAsync(folder, cancellationToken);
            var since = continuation is null ? BackfillStart() : (DateTimeOffset?)null;
            var fromStoredToken = continuation is not null;
            var total = new Result(0, 0, 0);

            while (true)
            {
                MailBatch batch;
                try
                {
                    batch = await source.GetChangesAsync(folder, continuation, since, cancellationToken);
                }
                catch (MailDeltaExpiredException) when (fromStoredToken)
                {
                    // The provider forgot the token: start over; messages already logged are recognized by their Message-ID.
                    await ResetStateAsync(folder, cancellationToken);
                    continuation = null;
                    since = BackfillStart();
                    fromStoredToken = false;
                    continue;
                }
                catch (MailSourceException ex)
                {
                    return Error.Failure("Mail.SourceFailed", $"Das Postfach konnte nicht gelesen werden: {ex.Message}");
                }

                fromStoredToken = false;
                var page = await ProcessPageAsync(batch.Messages, cancellationToken);
                total = new Result(total.Created + page.Created, total.Duplicates + page.Duplicates, total.Unmatched + page.Unmatched);

                if (batch.NextPageToken is not null)
                {
                    continuation = batch.NextPageToken;
                    since = null;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(batch.DeltaToken))
                {
                    return Error.Failure("Mail.DeltaTokenMissing", "Das Postfach hat keinen Fortsetzungspunkt geliefert.");
                }

                await SaveDeltaTokenAsync(folder, batch.DeltaToken, cancellationToken);
                return total;
            }
        }

        private DateTimeOffset BackfillStart() => clock.UtcNow.AddDays(-Math.Max(0, options.BackfillDays));

        private async Task<string?> LoadDeltaTokenAsync(MailFolder folder, CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            return await db.MailSyncStates
                .Where(s => s.Provider == source.Provider && s.Folder == folder)
                .Select(s => s.DeltaToken)
                .SingleOrDefaultAsync(cancellationToken);
        }

        private async Task ResetStateAsync(MailFolder folder, CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var state = await db.MailSyncStates.SingleOrDefaultAsync(s => s.Provider == source.Provider && s.Folder == folder, cancellationToken);
            if (state is not null)
            {
                state.Reset();
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        private async Task SaveDeltaTokenAsync(MailFolder folder, string deltaToken, CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var state = await db.MailSyncStates.SingleOrDefaultAsync(s => s.Provider == source.Provider && s.Folder == folder, cancellationToken);
            if (state is null)
            {
                state = new MailSyncState(source.Provider, folder);
                db.MailSyncStates.Add(state);
            }

            state.Advance(deltaToken, clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
        }

        private async Task<Result> ProcessPageAsync(IReadOnlyList<IncomingMail> messages, CancellationToken cancellationToken)
        {
            if (messages.Count == 0)
            {
                return new Result(0, 0, 0);
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var messageIds = messages.Select(MessageId).Distinct(StringComparer.Ordinal).ToList();
            var known = (await db.MailMessages
                .Where(m => m.Provider == source.Provider && messageIds.Contains(m.InternetMessageId))
                .Select(m => m.InternetMessageId)
                .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

            // contacts.email is citext, so the comparison ignores case.
            var addresses = messages.SelectMany(Addresses).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var contacts = (await db.Contacts
                .AsNoTracking()
                .Where(c => !c.IsArchived && c.Email != null && addresses.Contains(c.Email))
                .Select(c => new { c.Id, Email = c.Email! })
                .ToListAsync(cancellationToken))
                .ToDictionary(c => c.Email, c => c.Id, StringComparer.OrdinalIgnoreCase);

            int created = 0, duplicates = 0, unmatched = 0;
            foreach (var mail in messages)
            {
                var id = MessageId(mail);
                if (!known.Add(id))
                {
                    duplicates++;
                    continue;
                }

                if (MainContact(mail, contacts) is not { } contactId)
                {
                    known.Remove(id);
                    unmatched++;
                    continue;
                }

                var activity = Activity.LogMail(
                    mail.Folder == MailFolder.Sent ? ActivityDirection.Outbound : ActivityDirection.Inbound,
                    mail.OccurredAt,
                    Truncate(mail.Subject, SubjectMaxLength),
                    BodyOf(mail),
                    new LinkedRecords(ContactId: contactId));
                if (!options.RaiseActivityLoggedEvents)
                {
                    activity.ClearDomainEvents();
                }

                db.Activities.Add(activity);
                db.MailMessages.Add(new MailMessage(activity.Id, source.Provider, mail.ExternalId, id, mail.Folder));
                created++;
            }

            await db.SaveChangesAsync(cancellationToken);
            return new Result(created, duplicates, unmatched);
        }

        private static string MessageId(IncomingMail mail) =>
            string.IsNullOrWhiteSpace(mail.InternetMessageId) ? $"external:{mail.ExternalId}" : mail.InternetMessageId.Trim();

        private static IEnumerable<string> Addresses(IncomingMail mail) =>
            (mail.Sender is null ? mail.Recipients : mail.Recipients.Prepend(mail.Sender))
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a.Trim());

        /// <summary>Incoming: the sender if known, else the first known recipient. Outgoing: the first known recipient.</summary>
        private static Guid? MainContact(IncomingMail mail, Dictionary<string, Guid> contacts)
        {
            if (mail.Folder != MailFolder.Sent && mail.Sender is { } sender && contacts.TryGetValue(sender.Trim(), out var senderId))
            {
                return senderId;
            }

            foreach (var recipient in mail.Recipients)
            {
                if (!string.IsNullOrWhiteSpace(recipient) && contacts.TryGetValue(recipient.Trim(), out var id))
                {
                    return id;
                }
            }

            return null;
        }

        private static string BodyOf(IncomingMail mail)
        {
            var text = MailTextCleaner.Clean(mail.Body, mail.BodyIsHtml, mail.Subject);
            return text.Length == 0 ? EmptyBodyText : Truncate(text, BodyMaxLength)!;
        }

        private static string? Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : string.Concat(trimmed.AsSpan(0, maxLength - 1), "…");
        }
    }
}
