using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using SoloCrm.Domain.Common;
using SoloCrm.Infrastructure.Persistence;
using SoloCrm.Infrastructure.Persistence.Interceptors;

namespace SoloCrm.IntegrationTests.Persistence;

/// <summary>
/// Own database per test class instance with the CRM model plus a probe entity, wired with all interceptors.
/// </summary>
public sealed class ProbeDatabase(PostgresFixture postgres, FakeTimeProvider time)
{
    private readonly string _connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString)
    {
        Database = $"probe_{Guid.NewGuid():N}",
    }.ConnectionString;

    public async Task EnsureCreatedAsync(CancellationToken ct)
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync(ct);
    }

    public ProbeDbContext CreateContext() => new(new DbContextOptionsBuilder<ProbeDbContext>()
        .UseNpgsql(_connectionString)
        .UseSnakeCaseNamingConvention()
        .AddInterceptors(new TimestampInterceptor(time), new AuditInterceptor(time), new OutboxInterceptor(time))
        .Options);
}

public sealed class ProbeDbContext(DbContextOptions options) : CrmDbContext(options)
{
    public DbSet<Probe> Probes => Set<Probe>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Probe>().HasIndex(p => p.Name).IsUnique();
    }
}

public sealed class Probe : ArchivableEntity, IAuditable, IHasExtraFields
{
    public Probe(string name, int? size = null)
    {
        Name = name;
        Size = size;
    }

    public string Name { get; private set; }

    public int? Size { get; private set; }

    public IReadOnlyDictionary<string, string> ExtraFields { get; private set; } = new Dictionary<string, string>();

    public void Change(string name, int? size)
    {
        Name = name;
        Size = size;
    }

    public void SetExtraField(string key, string value) =>
        ExtraFields = new Dictionary<string, string>(ExtraFields) { [key] = value };

    public void Raise(IDomainEvent domainEvent) => AddDomainEvent(domainEvent);
}

public sealed record ProbeRenamed(Guid ProbeId, string Name) : IDomainEvent;
