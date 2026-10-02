using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SoloCrm.Infrastructure.Persistence.Seeding;
using SoloCrm.IntegrationTests.Web;

namespace SoloCrm.IntegrationTests.Persistence;

/// <summary>Migrations that transform existing data.</summary>
[Trait("Category", "Integration")]
public sealed class MigrationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AddOpportunityReceivedOn_ExistingOpportunity_ReceivedOnIsLocalCreationDate()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString)
        {
            Database = $"migration_{Guid.NewGuid():N}",
        }.ConnectionString;
        await using var db = CrmWebApplicationFactory.CreateDbContext(connectionString);
        var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
        await migrator.MigrateAsync("AddAddresses", cancellationToken: Ct);

        // 23:30 UTC is already the next day in Berlin (CEST).
        var id = Guid.CreateVersion7();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO opportunities (id, title, stage_id, is_archived, created_at, updated_at)
            VALUES ({id}, 'Migration', {DefaultStages.New}, false, '2026-09-30T23:30:00Z', '2026-09-30T23:30:00Z')
            """, Ct);

        await migrator.MigrateAsync(cancellationToken: Ct);

        var receivedOn = await db.Database
            .SqlQuery<DateOnly>($"SELECT received_on AS \"Value\" FROM opportunities WHERE id = {id}")
            .SingleAsync(Ct);
        receivedOn.Should().Be(new DateOnly(2026, 10, 1));
        (await db.Opportunities.SingleAsync(Ct)).ReceivedOn.Should().Be(new DateOnly(2026, 10, 1));
    }
}
