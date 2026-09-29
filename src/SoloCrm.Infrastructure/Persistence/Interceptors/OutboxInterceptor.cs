using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SoloCrm.Domain.Common;
using SoloCrm.Infrastructure.Persistence.Outbox;

namespace SoloCrm.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Moves the domain events of all tracked entities into the outbox within the same <c>SaveChanges</c>
/// call (and therefore transaction) as the change that raised them (ADR-010).
/// </summary>
public sealed class OutboxInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        AddOutboxMessages(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        AddOutboxMessages(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AddOutboxMessages(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var entities = context.ChangeTracker.Entries<Entity>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Count > 0)
            .ToList();

        if (entities.Count == 0)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        foreach (var entity in entities)
        {
            context.Set<OutboxMessage>().AddRange(entity.DomainEvents.Select(e => OutboxMessage.Create(e, now)));

            // Cleared right away: the messages are tracked now, so a retried SaveChanges does not duplicate them.
            entity.ClearDomainEvents();
        }
    }
}
