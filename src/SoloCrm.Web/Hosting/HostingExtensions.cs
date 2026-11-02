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

    /// <summary>The private address ranges; Traefik runs in one of them in the Coolify Docker network.</summary>
    public static readonly string[] DefaultKnownNetworks = ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"];

    /// <summary>
    /// Trusts X-Forwarded-* headers from the reverse proxy (Traefik in Coolify), at most one hop.
    /// Trusted proxies and networks come from the "ForwardedHeaders" configuration section, as array or comma-separated
    /// (<c>ForwardedHeaders__KnownNetworks=10.0.1.0/24</c>); without networks and proxies the private ranges are trusted.
    /// <c>X-Forwarded-Host</c> is only accepted for the hosts in <c>AllowedHosts</c>.
    /// </summary>
    public static IServiceCollection AddReverseProxySupport(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("ForwardedHeaders");
        var knownNetworks = ReadList(section.GetSection("KnownNetworks"));
        var knownProxies = ReadList(section.GetSection("KnownProxies"));
        if (knownNetworks.Length == 0 && knownProxies.Length == 0)
        {
            knownNetworks = DefaultKnownNetworks;
        }

        // Host filtering checks the Host header before X-Forwarded-Host replaces it, so the forwarded host is checked here.
        var allowedHosts = (configuration["AllowedHosts"] ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(h => h != "*")
            .ToList();

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto
                | ForwardedHeaders.XForwardedHost;
            options.ForwardLimit = 1;
            options.AllowedHosts = allowedHosts;

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
    /// Production with <c>AllowedHosts=*</c> accepts any Host header (SOL-34, audit F-02); <c>deploy/coolify.md</c>
    /// sets it to the own domain.
    /// </summary>
    public static void WarnIfAllHostsAllowed(this WebApplication app)
    {
        if (app.Environment.IsProduction() && app.Configuration["AllowedHosts"]?.Trim() is null or "" or "*")
        {
            LogAllHostsAllowed(app.Logger);
        }
    }

    // A comma-separated value replaces the whole list; an environment variable "…__0" would only replace the first entry.
    private static string[] ReadList(IConfigurationSection section) =>
        !string.IsNullOrWhiteSpace(section.Value)
            ? section.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [.. section.GetChildren().Select(c => c.Value?.Trim()).Where(v => !string.IsNullOrEmpty(v)).Select(v => v!)];

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "AllowedHosts is not restricted; set it to the app's domain (deploy/coolify.md)")]
    private static partial void LogAllHostsAllowed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Development: applying {Count} pending migration(s): {Migrations}")]
    private static partial void LogApplyingMigrations(ILogger logger, int count, IEnumerable<string> migrations);
}
