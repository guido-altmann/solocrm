using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Common;
using SoloCrm.Domain.Nextcloud;

namespace SoloCrm.Application.Features.Nextcloud;

/// <summary>
/// Mirrors the CRM to Nextcloud (ADR-014): every non-archived contact becomes a vCard in the address book
/// „SoloCRM“; archived and deleted contacts are removed there. SoloCRM is the master. Each run renders the whole
/// scope and writes only records whose payload hash differs from the one sent last, so derived changes (e.g. a
/// renamed organization in <c>ORG</c>) arrive as well. A rejected password pauses the sync until new credentials
/// are saved; other failures are retried on the next run.
/// </summary>
public static class SyncNextcloud
{
    public sealed record Command;

    /// <param name="Written">Records created or updated in Nextcloud.</param>
    /// <param name="Deleted">Copies removed from Nextcloud.</param>
    /// <param name="Unchanged">Records whose copy was up to date.</param>
    public sealed record Result(int Written, int Deleted, int Unchanged);

    public sealed class Handler(ICrmDbContextFactory dbFactory, IDavSyncTarget target, ISecretProtector protector, TimeProvider timeProvider)
        : ICommandHandler<Command, Result>
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
            var run = new Run(db, target, account);
            try
            {
                if (connection.AddressBookHref is null)
                {
                    connection.UseAddressBook(await target.EnsureAddressBookAsync(account, cancellationToken));
                    await db.SaveChangesAsync(cancellationToken);
                }

                await run.SyncContactsAsync(connection.AddressBookHref!, cancellationToken);
                connection.RecordSuccess(timeProvider.GetUtcNow());
                await db.SaveChangesAsync(cancellationToken);
                return new Result(run.Written, run.Deleted, run.Unchanged);
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
    private sealed class Run(ICrmDbContext db, IDavSyncTarget target, DavAccount account)
    {
        public int Written { get; private set; }

        public int Deleted { get; private set; }

        public int Unchanged { get; private set; }

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
                var content = ContactVCard.Render(card);
                var item = items.GetValueOrDefault(card.Id);
                var href = item?.Href ?? CollectionHref.Combine(addressBookHref, ContactVCard.ResourceName(card.Id));
                await WriteAsync(NextcloudItemType.Contact, card.Id, item, href, content, cancellationToken);
            }

            var inScope = cards.Select(c => c.Id).ToHashSet();
            await DeleteAsync(items.Values.Where(i => !inScope.Contains(i.EntityId)), cancellationToken);
        }

        private async Task WriteAsync(
            NextcloudItemType type, Guid id, NextcloudSyncItem? item, string href, DavContent content, CancellationToken cancellationToken)
        {
            var hash = PayloadHash.Of(content.Body);
            if (item?.ContentHash == hash)
            {
                Unchanged++;
                return;
            }

            var result = await target.PutAsync(account, href, content, item?.ETag, unconditional: false, cancellationToken);
            if (result.PreconditionFailed)
            {
                // Changed or deleted in Nextcloud: SoloCRM is the master for contacts.
                result = await target.PutAsync(account, href, content, ifMatch: null, unconditional: true, cancellationToken);
            }

            if (item is null)
            {
                item = new NextcloudSyncItem(type, id, href);
                db.NextcloudSyncItems.Add(item);
            }

            item.Written(result.ETag, hash);
            await db.SaveChangesAsync(cancellationToken);
            Written++;
        }

        private async Task DeleteAsync(IEnumerable<NextcloudSyncItem> items, CancellationToken cancellationToken)
        {
            foreach (var item in items.ToList())
            {
                await target.DeleteAsync(account, item.Href, cancellationToken);
                db.NextcloudSyncItems.Remove(item);
                await db.SaveChangesAsync(cancellationToken);
                Deleted++;
            }
        }
    }
}
