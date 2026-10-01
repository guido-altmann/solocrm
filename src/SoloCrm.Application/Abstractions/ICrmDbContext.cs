using Microsoft.EntityFrameworkCore;
using SoloCrm.Domain.Activities;
using SoloCrm.Domain.ApiKeys;
using SoloCrm.Domain.Auditing;
using SoloCrm.Domain.Contacts;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Domain.Organizations;
using SoloCrm.Domain.Tags;
using SoloCrm.Domain.Tasks;
using SoloCrm.Domain.Webhooks;

namespace SoloCrm.Application.Abstractions;

/// <summary>
/// Unit of work used by handlers. Entity sets are added per feature.
/// </summary>
public interface ICrmDbContext : IAsyncDisposable, IDisposable
{
    DbSet<Contact> Contacts { get; }

    DbSet<Organization> Organizations { get; }

    DbSet<Opportunity> Opportunities { get; }

    DbSet<Stage> Stages { get; }

    DbSet<Activity> Activities { get; }

    DbSet<TaskItem> Tasks { get; }

    DbSet<Tag> Tags { get; }

    DbSet<ContactTag> ContactTags { get; }

    DbSet<OrganizationTag> OrganizationTags { get; }

    DbSet<OpportunityTag> OpportunityTags { get; }

    /// <summary>Read by the timeline; entries are written exclusively by the audit interceptor.</summary>
    DbSet<AuditEntry> AuditEntries { get; }

    DbSet<WebhookSubscription> WebhookSubscriptions { get; }

    /// <summary>Read by the delivery log; entries are written by the outbox processor and the ping.</summary>
    DbSet<WebhookDelivery> WebhookDeliveries { get; }

    DbSet<ApiKey> ApiKeys { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Creates a short-lived <see cref="ICrmDbContext"/> per handler call. Blazor Server
/// scopes live as long as the circuit, so handlers must not hold a scoped context.
/// </summary>
public interface ICrmDbContextFactory
{
    Task<ICrmDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default);
}
