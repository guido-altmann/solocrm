using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Common;
using SoloCrm.Application.Features.Tasks;
using SoloCrm.Domain.Nextcloud;

namespace SoloCrm.Application.Features.Nextcloud;

/// <summary>
/// Mirrors the CRM to Nextcloud (ADR-014). SoloCRM is the master: every non-archived contact becomes a vCard in the
/// address book „SoloCRM“, every open task and every task completed in the last 30 days a <c>VTODO</c> in the task
/// list „SoloCRM“; records leaving that scope are removed there. Each run first takes over tasks completed or
/// reopened in Nextcloud (via <see cref="CompleteTask"/>/<see cref="ReopenTask"/>, so with audit and events), then
/// renders the whole scope and writes only records whose payload hash differs from the one sent last, so derived
/// changes (e.g. a renamed organization in <c>ORG</c>) arrive as well and other edits in Nextcloud are overwritten.
/// A rejected password pauses the sync until new credentials are saved; other failures are retried on the next run.
/// </summary>
public static class SyncNextcloud
{
    public sealed record Command;

    /// <param name="Written">Records created or updated in Nextcloud.</param>
    /// <param name="Deleted">Copies removed from Nextcloud.</param>
    /// <param name="Unchanged">Records whose copy was up to date.</param>
    /// <param name="Completed">Tasks completed because they were checked off in Nextcloud.</param>
    /// <param name="Reopened">Tasks reopened because they were unchecked in Nextcloud.</param>
    /// <param name="Skipped">Tasks not written because Nextcloud changed them meanwhile; the next run takes them over.</param>
    public sealed record Result(int Written, int Deleted, int Unchanged, int Completed, int Reopened, int Skipped);

    public sealed class Handler(
        ICrmDbContextFactory dbFactory,
        IDavSyncTarget target,
        ISecretProtector protector,
        ICommandHandler<CompleteTask.Command, CompleteTask.Result> completeTask,
        ICommandHandler<ReopenTask.Command, ReopenTask.Result> reopenTask,
        NextcloudSyncOptions options,
        TimeProvider timeProvider) : ICommandHandler<Command, Result>
    {
        // One sync at a time: the background service and "Jetzt abgleichen" must not write the same records twice.
        private static readonly SemaphoreSlim Gate = new(1, 1);

        public async Task<Result<Result>> Handle(Command command, CancellationToken cancellationToken)
        {
            await Gate.WaitAsync(cancellationToken);
            try
            {
                return await SyncAsync(cancellationToken);
            }
            finally
            {
                Gate.Release();
            }
        }

        private async Task<Result<Result>> SyncAsync(CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var connection = await db.NextcloudConnections.SingleOrDefaultAsync(cancellationToken);
            if (connection is null)
            {
                return NextcloudErrors.NotConnected;
            }

            if (connection.IsCredentialRejected)
            {
                return NextcloudErrors.CredentialRejected;
            }

            string password;
            try
            {
                password = protector.Unprotect(connection.ProtectedAppPassword);
            }
            catch (CryptographicException)
            {
                connection.RejectCredentials(NextcloudErrors.UnreadablePassword.Message);
                await db.SaveChangesAsync(cancellationToken);
                return NextcloudErrors.UnreadablePassword;
            }

            var account = new DavAccount(new Uri(connection.ServerUrl), connection.UserName, password);
            var run = new Run(db, target, account, completeTask, reopenTask);
            try
            {
                if (connection.AddressBookHref is null)
                {
                    connection.UseAddressBook(await target.EnsureAddressBookAsync(account, cancellationToken));
                }

                if (connection.TaskListHref is null)
                {
                    connection.UseTaskList(await target.EnsureTaskListAsync(account, cancellationToken));
                }

                await db.SaveChangesAsync(cancellationToken);

                // Incoming first: a task checked off on the phone must not be overwritten as open by the outgoing phase.
                var token = await run.TakeOverTaskChangesAsync(connection.TaskListHref!, connection.TaskSyncToken, cancellationToken);
                connection.AdvanceTaskSyncToken(token);
                await db.SaveChangesAsync(cancellationToken);

                await run.SyncContactsAsync(connection.AddressBookHref!, cancellationToken);
                await run.SyncTasksAsync(connection.TaskListHref!, options.AppBaseUrl, timeProvider.GetUtcNow(), cancellationToken);
                connection.RecordSuccess(timeProvider.GetUtcNow());
                await db.SaveChangesAsync(cancellationToken);
                return run.ToResult();
            }
            catch (DavUnauthorizedException ex)
            {
                connection.RejectCredentials(ex.Message);
                await db.SaveChangesAsync(cancellationToken);
                return Error.Failure("Nextcloud.Unauthorized", ex.Message);
            }
            catch (DavException ex)
            {
                connection.RecordFailure(ex.Message);
                await db.SaveChangesAsync(cancellationToken);
                return Error.Failure("Nextcloud.SyncFailed", ex.Message);
            }
        }
    }

    /// <summary>One sync run with its counters; the mapping is saved after every write, so an aborted run loses nothing.</summary>
    private sealed class Run(
        ICrmDbContext db,
        IDavSyncTarget target,
        DavAccount account,
        ICommandHandler<CompleteTask.Command, CompleteTask.Result> completeTask,
        ICommandHandler<ReopenTask.Command, ReopenTask.Result> reopenTask)
    {
        private int _written;
        private int _deleted;
        private int _unchanged;
        private int _completed;
        private int _reopened;
        private int _skipped;

        public Result ToResult() => new(_written, _deleted, _unchanged, _completed, _reopened, _skipped);

        /// <summary>
        /// Incoming phase (tasks only): takes over the completion state of tasks changed in Nextcloud. Changes with the
        /// stored ETag are the own writes; deleted copies are written again by the outgoing phase. Entries SoloCRM does
        /// not know are left alone. Returns the token for the next run.
        /// </summary>
        public async Task<string?> TakeOverTaskChangesAsync(string taskListHref, string? syncToken, CancellationToken cancellationToken)
        {
            DavChanges changes;
            try
            {
                changes = await target.GetChangesAsync(account, taskListHref, syncToken, cancellationToken);
            }
            catch (DavSyncTokenExpiredException) when (syncToken is not null)
            {
                // Without a token the server lists all entries; comparing the ETags finds what changed.
                changes = await target.GetChangesAsync(account, taskListHref, null, cancellationToken);
            }

            var hrefs = changes.Changes.Select(c => c.Href).ToList();
            var items = await db.NextcloudSyncItems
                .Where(i => i.EntityType == NextcloudItemType.Task && hrefs.Contains(i.Href))
                .ToDictionaryAsync(i => i.Href, StringComparer.Ordinal, cancellationToken);

            foreach (var change in changes.Changes)
            {
                if (!items.TryGetValue(change.Href, out var item) || (!change.Deleted && change.ETag == item.ETag))
                {
                    continue;
                }

                var body = change.Deleted ? null : await target.GetAsync(account, change.Href, cancellationToken);
                if (body is null)
                {
                    // Deleted in Nextcloud: SoloCRM is the master, the outgoing phase creates the copy again.
                    db.NextcloudSyncItems.Remove(item);
                }
                else
                {
                    await TakeOverStatusAsync(item.EntityId, TaskVTodo.IsCompleted(body), cancellationToken);
                    item.ChangedRemotely(change.ETag);
                }

                await db.SaveChangesAsync(cancellationToken);
            }

            return changes.SyncToken;
        }

        public async Task SyncContactsAsync(string addressBookHref, CancellationToken cancellationToken)
        {
            var cards = await db.Contacts
                .AsNoTracking()
                .Where(c => !c.IsArchived)
                .OrderBy(c => c.Id)
                .Select(c => new ContactVCard.Card(
                    c.Id,
                    c.FirstName,
                    c.LastName,
                    c.Email,
                    c.Phone,
                    c.JobTitle,
                    c.LinkedInUrl,
                    c.Organization != null ? c.Organization.Name : null,
                    new AddressData(c.Address.Street, c.Address.Street2, c.Address.PostalCode, c.Address.City, c.Address.Region, c.Address.CountryCode)))
                .ToListAsync(cancellationToken);

            var items = await db.NextcloudSyncItems
                .Where(i => i.EntityType == NextcloudItemType.Contact)
                .ToDictionaryAsync(i => i.EntityId, cancellationToken);

            foreach (var card in cards)
            {
                var item = items.GetValueOrDefault(card.Id);
                var href = item?.Href ?? CollectionHref.Combine(addressBookHref, ContactVCard.ResourceName(card.Id));
                await WriteAsync(NextcloudItemType.Contact, card.Id, item, href, ContactVCard.Render(card), cancellationToken);
            }

            var inScope = cards.Select(c => c.Id).ToHashSet();
            await DeleteAsync(items.Values.Where(i => !inScope.Contains(i.EntityId)), cancellationToken);
        }

        public async Task SyncTasksAsync(string taskListHref, string? baseUrl, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var cutoff = now.AddDays(-TaskVTodo.CompletedRetentionDays);
            var tasks = await db.Tasks
                .AsNoTracking()
                .Where(t => t.CompletedAt == null || t.CompletedAt >= cutoff)
                .OrderBy(t => t.Id)
                .Select(t => new TaskVTodo.Item(t.Id, t.Title, t.DueDate, t.CompletedAt, t.CreatedAt, t.UpdatedAt, t.ContactId, t.OrganizationId, t.OpportunityId))
                .ToListAsync(cancellationToken);

            var items = await db.NextcloudSyncItems
                .Where(i => i.EntityType == NextcloudItemType.Task)
                .ToDictionaryAsync(i => i.EntityId, cancellationToken);

            foreach (var task in tasks)
            {
                var item = items.GetValueOrDefault(task.Id);
                var href = item?.Href ?? CollectionHref.Combine(taskListHref, TaskVTodo.ResourceName(task.Id));
                await WriteAsync(NextcloudItemType.Task, task.Id, item, href, TaskVTodo.Render(task, baseUrl), cancellationToken);
            }

            var inScope = tasks.Select(t => t.Id).ToHashSet();
            await DeleteAsync(items.Values.Where(i => !inScope.Contains(i.EntityId)), cancellationToken);
        }

        private async Task TakeOverStatusAsync(Guid taskId, bool? remoteCompleted, CancellationToken cancellationToken)
        {
            if (remoteCompleted is not { } completed)
            {
                return;
            }

            var localCompleted = await db.Tasks.Where(t => t.Id == taskId).Select(t => (bool?)(t.CompletedAt != null)).SingleOrDefaultAsync(cancellationToken);
            if (localCompleted is null || localCompleted == completed)
            {
                // Deleted in SoloCRM meanwhile (the outgoing phase removes the copy) or nothing to take over.
                return;
            }

            if (completed)
            {
                if ((await completeTask.Handle(new CompleteTask.Command(taskId), cancellationToken)).IsSuccess)
                {
                    _completed++;
                }
            }
            else if ((await reopenTask.Handle(new ReopenTask.Command(taskId), cancellationToken)).IsSuccess)
            {
                _reopened++;
            }
        }

        private async Task WriteAsync(
            NextcloudItemType type, Guid id, NextcloudSyncItem? item, string href, DavContent content, CancellationToken cancellationToken)
        {
            var hash = PayloadHash.Of(content.Body);
            if (item?.ContentHash == hash)
            {
                _unchanged++;
                return;
            }

            var result = await target.PutAsync(account, href, content, item?.ETag, unconditional: false, cancellationToken);
            if (result.PreconditionFailed)
            {
                if (type == NextcloudItemType.Task)
                {
                    // Changed in Nextcloud since the incoming phase, e.g. just checked off: the next run takes it over.
                    _skipped++;
                    return;
                }

                // SoloCRM is the master for contacts.
                result = await target.PutAsync(account, href, content, ifMatch: null, unconditional: true, cancellationToken);
            }

            if (item is null)
            {
                item = new NextcloudSyncItem(type, id, href);
                db.NextcloudSyncItems.Add(item);
            }

            item.Written(result.ETag, hash);
            await db.SaveChangesAsync(cancellationToken);
            _written++;
        }

        private async Task DeleteAsync(IEnumerable<NextcloudSyncItem> items, CancellationToken cancellationToken)
        {
            foreach (var item in items.ToList())
            {
                await target.DeleteAsync(account, item.Href, cancellationToken);
                db.NextcloudSyncItems.Remove(item);
                await db.SaveChangesAsync(cancellationToken);
                _deleted++;
            }
        }
    }
}
