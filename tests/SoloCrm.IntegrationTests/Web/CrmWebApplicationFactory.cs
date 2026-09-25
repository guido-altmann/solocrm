using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SoloCrm.Infrastructure.Persistence;

namespace SoloCrm.IntegrationTests.Web;

/// <summary>
/// Hosts the web app against its own database on the shared Postgres container.
/// </summary>
public sealed class CrmWebApplicationFactory(string connectionString, string? adminEmail, string? adminPassword)
    : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@example.test";
    public const string AdminPassword = "Initial-Passw0rd!";

    public string ConnectionString { get; } = connectionString;

    /// <summary>Creates a fresh database with the current model (no migrations exist yet).</summary>
    public static async Task<string> CreateDatabaseAsync(PostgresFixture postgres, CancellationToken ct)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString)
        {
            Database = $"web_{Guid.NewGuid():N}",
        }.ConnectionString;

        await using var db = CreateDbContext(connectionString);
        await db.Database.EnsureCreatedAsync(ct);

        return connectionString;
    }

    public static CrmDbContext CreateDbContext(string connectionString) => new(new DbContextOptionsBuilder<CrmDbContext>()
        .UseNpgsql(connectionString)
        .UseSnakeCaseNamingConvention()
        .Options);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Crm", ConnectionString);
        builder.UseSetting("DataProtection:KeysPath", "");
        // Explicit values (also when empty) so local user secrets never leak into tests.
        builder.UseSetting("Admin:Email", adminEmail ?? "");
        builder.UseSetting("Admin:InitialPassword", adminPassword ?? "");
    }
}
