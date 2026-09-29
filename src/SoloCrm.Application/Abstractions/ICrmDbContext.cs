using Microsoft.EntityFrameworkCore;
using SoloCrm.Domain.Contacts;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Domain.Organizations;

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
