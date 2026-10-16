using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Nextcloud;

namespace SoloCrm.Application.Features.Nextcloud;

/// <summary>State of the Nextcloud connection for the settings page (ADR-014).</summary>
public static class GetNextcloudStatus
{
    public sealed record Query;

    /// <param name="Connection">The connected account, or <c>null</c>.</param>
    public sealed record Result(Connection? Connection);

    /// <param name="IsCredentialRejected">The server rejected the app password; the sync is paused.</param>
    /// <param name="Contacts">Contacts currently mirrored to the address book.</param>
    public sealed record Connection(
        string ServerUrl,
        string UserName,
        DateTimeOffset ConnectedAt,
        DateTimeOffset? LastSyncedAt,
        string? LastError,
        bool IsCredentialRejected,
        int Contacts);

    public sealed class Handler(ICrmDbContextFactory dbFactory) : IQueryHandler<Query, Result>
    {
        public async Task<Result<Result>> Handle(Query query, CancellationToken cancellationToken)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var connection = await db.NextcloudConnections.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (connection is null)
            {
                return new Result(null);
            }

            var contacts = await db.NextcloudSyncItems.CountAsync(i => i.EntityType == NextcloudItemType.Contact, cancellationToken);
            return new Result(new Connection(
                connection.ServerUrl,
                connection.UserName,
                connection.ConnectedAt,
                connection.LastSyncedAt,
                connection.LastError,
                connection.IsCredentialRejected,
                contacts));
        }
    }
}
