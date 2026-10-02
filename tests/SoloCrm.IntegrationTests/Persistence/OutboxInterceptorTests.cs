using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace SoloCrm.IntegrationTests.Persistence;

[Trait("Category", "Integration")]
public sealed class OutboxInterceptorTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 9, 0, 0, TimeSpan.Zero);

    private readonly ProbeDatabase _database;

    public OutboxInterceptorTests(PostgresFixture postgres)
    {
        _database = new ProbeDatabase(postgres, new FakeTimeProvider(Now));
    }

    public async ValueTask InitializeAsync() => await _database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task SaveChanges_EntityWithEvents_WritesOutboxMessagesAndClearsEvents()
    {
        var ct = TestContext.Current.CancellationToken;
        var probe = new Probe("first");
        probe.Raise(new ProbeRenamed(probe.Id, "first"));
        probe.Raise(new ProbeRenamed(probe.Id, "second"));

        await using (var db = _database.CreateContext())
        {
            db.Probes.Add(probe);
            await db.SaveChangesAsync(ct);
        }

        probe.DomainEvents.Should().BeEmpty();
        await using var verify = _database.CreateContext();
        var messages = await verify.OutboxMessages.OrderBy(m => m.Id).ToListAsync(ct);
        messages.Select(m => m.Type).Should().Equal("probe.renamed", "probe.renamed");
        // UUIDv7 ids created within the same millisecond are not ordered, so the order is not asserted.
        messages.Select(m => m.Payload).Should().BeEquivalentTo(
            $$"""{"name": "first", "probeId": "{{probe.Id}}"}""",
            $$"""{"name": "second", "probeId": "{{probe.Id}}"}""");
        messages.Should().AllSatisfy(m =>
        {
            m.OccurredAt.Should().Be(Now);
            m.NextAttemptAt.Should().Be(Now);
            m.ProcessedAt.Should().BeNull();
        });
    }

    [Fact]
    public async Task SaveChanges_FailingSave_PersistsNoOutboxMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var db = _database.CreateContext())
        {
            db.Probes.Add(new Probe("taken"));
            await db.SaveChangesAsync(ct);
        }

        var probe = new Probe("fresh");
        probe.Raise(new ProbeRenamed(probe.Id, "fresh"));
        await using (var db = _database.CreateContext())
        {
            db.Probes.AddRange(probe, new Probe("taken"));
            var act = () => db.SaveChangesAsync(ct);
            await act.Should().ThrowAsync<DbUpdateException>();
        }

        await using var verify = _database.CreateContext();
        (await verify.OutboxMessages.AnyAsync(ct)).Should().BeFalse();
        (await verify.Probes.AnyAsync(p => p.Name == "fresh", ct)).Should().BeFalse();
    }
}
