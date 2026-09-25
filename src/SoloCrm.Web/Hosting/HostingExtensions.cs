using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using SoloCrm.Infrastructure.Persistence;

namespace SoloCrm.Web.Hosting;

internal static class HostingExtensions
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
}
