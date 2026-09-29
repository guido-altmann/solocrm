using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Infrastructure.Persistence;

namespace SoloCrm.Web.Hosting;

internal static partial class HostingExtensions
{
    private const string ReadyTag = "ready";

    /// <summary>
    /// Trusts X-Forwarded-* headers from the reverse proxy (Traefik in Coolify).
    /// Trusted proxies and networks come from the "ForwardedHeaders" configuration section.
    /// </summary>
    public static IServiceCollection AddReverseProxySupport(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("ForwardedHeaders");
        var knownNetworks = section.GetSection("KnownNetworks").Get<string[]>() ?? [];
        var knownProxies = section.GetSection("KnownProxies").Get<string[]>() ?? [];

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto
                | ForwardedHeaders.XForwardedHost;

            foreach (var network in knownNetworks)
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }

            foreach (var proxy in knownProxies)
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            }
        });

        return services;
    }

    /// <summary>
    /// Persists Data Protection keys (auth cookies, antiforgery) so sessions survive redeploys.
    /// Without "DataProtection:KeysPath" the framework default key location is used (local development).
    /// </summary>
    public static IServiceCollection AddPersistentDataProtection(this IServiceCollection services, IConfiguration configuration)
    {
        var dataProtection = services.AddDataProtection().SetApplicationName("SoloCrm");

        var keysPath = configuration["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        return services;
    }

    public static IServiceCollection AddCrmHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddDbContextCheck<CrmDbContext>("postgres", tags: [ReadyTag]);

        return services;
    }

    /// <summary>
    /// /health/live: process is up (no dependencies). /health/ready: database is reachable.
    /// </summary>
    public static IEndpointRouteBuilder MapCrmHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous();

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains(ReadyTag) })
            .AllowAnonymous();

        return endpoints;
    }

    /// <summary>
    /// Development only: applies pending EF Core migrations before the app starts, so a pulled schema change
    /// needs no manual <c>dotnet ef database update</c>. Production migrates via <c>efbundle</c> in the
    /// container entrypoint (ADR-009).
    /// </summary>
    public static async Task MigrateDatabaseInDevelopmentAsync(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        var factory = app.Services.GetRequiredService<IDbContextFactory<CrmDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        LogApplyingMigrations(app.Logger, pending.Count, pending);
        await db.Database.MigrateAsync();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Development: applying {Count} pending migration(s): {Migrations}")]
    private static partial void LogApplyingMigrations(ILogger logger, int count, IEnumerable<string> migrations);
}
