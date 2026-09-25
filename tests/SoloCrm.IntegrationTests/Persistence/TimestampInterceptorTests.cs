using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using SoloCrm.Domain.Common;
using SoloCrm.Infrastructure.Persistence;
using SoloCrm.Infrastructure.Persistence.Interceptors;

namespace SoloCrm.IntegrationTests.Persistence;

[Trait("Category", "Integration")]
public sealed class TimestampInterceptorTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTimeOffset Start = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Start);

    // Each test gets its own database so tests stay independent.
    private readonly string _connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString)
    {
        Database = $"timestamps_{Guid.NewGuid():N}",
    }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task SaveChanges_AddedEntity_SetsCreatedAtAndUpdatedAt()
    {
        var ct = TestContext.Current.CancellationToken;
        var entity = new Probe("first");

        await using (var db = CreateContext())
        {
            db.Probes.Add(entity);
            await db.SaveChangesAsync(ct);
        }

        await using var verify = CreateContext();
        var stored = await verify.Probes.SingleAsync(p => p.Id == entity.Id, ct);
        stored.CreatedAt.Should().Be(Start);
        stored.UpdatedAt.Should().Be(Start);
    }

    [Fact]
    public async Task SaveChanges_ModifiedEntity_UpdatesOnlyUpdatedAt()
    {
        var ct = TestContext.Current.CancellationToken;
        var entity = new Probe("first");
        await using (var db = CreateContext())
        {
            db.Probes.Add(entity);
            await db.SaveChangesAsync(ct);
        }

        _time.Advance(TimeSpan.FromHours(1));
        await using (var db = CreateContext())
        {
            var loaded = await db.Probes.SingleAsync(p => p.Id == entity.Id, ct);
            loaded.Rename("second");
            await db.SaveChangesAsync(ct);
        }

        await using var verify = CreateContext();
        var stored = await verify.Probes.SingleAsync(p => p.Id == entity.Id, ct);
        stored.CreatedAt.Should().Be(Start);
        stored.UpdatedAt.Should().Be(Start.AddHours(1));
    }

    [Fact]
    public async Task SaveChanges_NewEntity_KeepsIdAssignedInConstructor()
    {
        var ct = TestContext.Current.CancellationToken;
        var entity = new Probe("first");
        var id = entity.Id;

        await using var db = CreateContext();
        db.Probes.Add(entity);
        await db.SaveChangesAsync(ct);

        entity.Id.Should().Be(id);
    }

    private ProbeDbContext CreateContext() => new(new DbContextOptionsBuilder<ProbeDbContext>()
        .UseNpgsql(_connectionString)
        .UseSnakeCaseNamingConvention()
        .AddInterceptors(new TimestampInterceptor(_time))
        .Options);

    private sealed class ProbeDbContext(DbContextOptions options) : CrmDbContext(options)
    {
        public DbSet<Probe> Probes => Set<Probe>();
    }

    private sealed class Probe(string name) : Entity
    {
        public string Name { get; private set; } = name;

        public void Rename(string name) => Name = name;
    }
}
