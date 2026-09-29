using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using SoloCrm.Domain.Auditing;

namespace SoloCrm.IntegrationTests.Persistence;

[Trait("Category", "Integration")]
public sealed class AuditInterceptorTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTimeOffset Start = new(2026, 9, 29, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Start);
    private readonly ProbeDatabase _database;

    public AuditInterceptorTests(PostgresFixture postgres)
    {
        _database = new ProbeDatabase(postgres, _time);
    }

    public async ValueTask InitializeAsync() => await _database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task SaveChanges_AddedEntity_WritesCreatedWithSetValues()
    {
        var ct = TestContext.Current.CancellationToken;
        var probe = new Probe("first");

        await SaveAsync(db => db.Probes.Add(probe), ct);

        var entry = (await LoadEntriesAsync(ct)).Should().ContainSingle().Subject;
        entry.EntityType.Should().Be(nameof(Probe));
        entry.EntityId.Should().Be(probe.Id);
        entry.Action.Should().Be(AuditAction.Created);
        entry.OccurredAt.Should().Be(Start);
        entry.Changes.Should().Equal(new AuditChange("Name", null, "first"));
    }

    [Fact]
    public async Task SaveChanges_ModifiedEntity_WritesUpdatedWithChangedFieldsOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        var probe = await AddProbeAsync(new Probe("first", 1), ct);

        _time.Advance(TimeSpan.FromMinutes(5));
        await SaveAsync(async db =>
        {
            var loaded = await db.Probes.SingleAsync(p => p.Id == probe.Id, ct);
            loaded.Change("second", 1);
            loaded.SetExtraField("industry", "IT");
        }, ct);

        var entry = (await LoadEntriesAsync(ct)).Should().ContainSingle(e => e.Action == AuditAction.Updated).Subject;
        entry.OccurredAt.Should().Be(Start.AddMinutes(5));
        entry.Changes.Should().BeEquivalentTo(
        [
            new AuditChange("Name", "first", "second"),
            new AuditChange("ExtraFields", "{}", """{"industry":"IT"}"""),
        ]);
    }

    [Fact]
    public async Task SaveChanges_ValueSetToNull_RecordsOldValue()
    {
        var ct = TestContext.Current.CancellationToken;
        var probe = await AddProbeAsync(new Probe("first", 3), ct);

        await SaveAsync(async db => (await db.Probes.SingleAsync(p => p.Id == probe.Id, ct)).Change("first", null), ct);

        (await LoadEntriesAsync(ct)).Should().ContainSingle(e => e.Action == AuditAction.Updated)
            .Which.Changes.Should().Equal(new AuditChange("Size", "3", null));
    }

    [Fact]
    public async Task SaveChanges_ModifiedWithoutEffectiveChange_WritesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var probe = await AddProbeAsync(new Probe("first"), ct);

        await SaveAsync(async db => (await db.Probes.SingleAsync(p => p.Id == probe.Id, ct)).Change("first", null), ct);

        (await LoadEntriesAsync(ct)).Should().ContainSingle().Which.Action.Should().Be(AuditAction.Created);
    }

    [Fact]
    public async Task SaveChanges_Archived_WritesArchivedInsteadOfUpdated()
    {
        var ct = TestContext.Current.CancellationToken;
        var probe = await AddProbeAsync(new Probe("first"), ct);

        await SaveAsync(async db => (await db.Probes.SingleAsync(p => p.Id == probe.Id, ct)).Archive(), ct);

        (await LoadEntriesAsync(ct)).Should().ContainSingle(e => e.Action == AuditAction.Archived)
            .Which.Changes.Should().Equal(new AuditChange("IsArchived", "false", "true"));
    }

    [Fact]
    public async Task SaveChanges_Restored_WritesUpdated()
    {
        var ct = TestContext.Current.CancellationToken;
        var probe = new Probe("first");
        probe.Archive();
        await AddProbeAsync(probe, ct);

        await SaveAsync(async db => (await db.Probes.SingleAsync(p => p.Id == probe.Id, ct)).Restore(), ct);

        (await LoadEntriesAsync(ct)).Should().ContainSingle(e => e.Action == AuditAction.Updated)
            .Which.Changes.Should().Equal(new AuditChange("IsArchived", "true", "false"));
    }

    [Fact]
    public async Task SaveChanges_DeletedEntity_WritesDeletedWithOldValues()
    {
        var ct = TestContext.Current.CancellationToken;
        var probe = await AddProbeAsync(new Probe("first", 2), ct);

        await SaveAsync(async db => db.Probes.Remove(await db.Probes.SingleAsync(p => p.Id == probe.Id, ct)), ct);

        (await LoadEntriesAsync(ct)).Should().ContainSingle(e => e.Action == AuditAction.Deleted)
            .Which.Changes.Should().BeEquivalentTo(
            [
                new AuditChange("Name", "first", null),
                new AuditChange("Size", "2", null),
                new AuditChange("IsArchived", "false", null),
                new AuditChange("ExtraFields", "{}", null),
            ]);
    }

    [Fact]
    public async Task SaveChanges_FailingSave_PersistsNoAuditEntry()
    {
        var ct = TestContext.Current.CancellationToken;
        await AddProbeAsync(new Probe("taken"), ct);

        var act = () => SaveAsync(db => db.Probes.AddRange(new Probe("fresh"), new Probe("taken")), ct);

        await act.Should().ThrowAsync<DbUpdateException>();
        (await LoadEntriesAsync(ct)).Should().ContainSingle();
    }

    private async Task<Probe> AddProbeAsync(Probe probe, CancellationToken ct)
    {
        await SaveAsync(db => db.Probes.Add(probe), ct);
        return probe;
    }

    private Task SaveAsync(Action<ProbeDbContext> change, CancellationToken ct) =>
        SaveAsync(db =>
        {
            change(db);
            return Task.CompletedTask;
        }, ct);

    private async Task SaveAsync(Func<ProbeDbContext, Task> change, CancellationToken ct)
    {
        await using var db = _database.CreateContext();
        await change(db);
        await db.SaveChangesAsync(ct);
    }

    private async Task<List<AuditEntry>> LoadEntriesAsync(CancellationToken ct)
    {
        await using var db = _database.CreateContext();
        return await db.AuditEntries.OrderBy(e => e.Id).ToListAsync(ct);
    }
}
