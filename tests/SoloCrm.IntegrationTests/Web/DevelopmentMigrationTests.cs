using Microsoft.EntityFrameworkCore;
using Npgsql;
using static SoloCrm.IntegrationTests.Web.CrmWebApplicationFactory;

namespace SoloCrm.IntegrationTests.Web;

/// <summary>In Development the app migrates its database on startup (production uses efbundle, ADR-009).</summary>
[Trait("Category", "Integration")]
[Collection(WebHostTests.Name)]
public sealed class DevelopmentMigrationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Start_DevelopmentWithEmptyDatabase_AppliesAllMigrations()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = $"dev_{Guid.NewGuid():N}";
        var connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = database }.ConnectionString;
        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync(ct);
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection);
            await create.ExecuteNonQueryAsync(ct);
        }

        await using var factory = new CrmWebApplicationFactory(connectionString, AdminEmail, AdminPassword);
        using var client = factory.CreateClient();

        await using var verify = CreateDbContext(connectionString);
        (await verify.Database.GetPendingMigrationsAsync(ct)).Should().BeEmpty();
        (await verify.Tasks.AnyAsync(ct)).Should().BeFalse("the tasks table exists");
    }
}
