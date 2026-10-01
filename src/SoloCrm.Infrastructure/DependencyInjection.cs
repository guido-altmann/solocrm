using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Webhooks;
using SoloCrm.Infrastructure.Identity;
using SoloCrm.Infrastructure.Persistence;
using SoloCrm.Infrastructure.Persistence.Interceptors;
using SoloCrm.Infrastructure.Persistence.Outbox;
using SoloCrm.Infrastructure.Settings;
using SoloCrm.Infrastructure.Webhooks;

namespace SoloCrm.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Crm");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'Crm' not found.");
        }

        services.TryAddSingleton(TimeProvider.System);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(configuration["App:TimeZone"] is { Length: > 0 } id ? id : AppClock.DefaultTimeZoneId);
        services.AddSingleton(sp => new AppClock(sp.GetRequiredService<TimeProvider>(), timeZone));
        services.AddSingleton<TimestampInterceptor>();
        services.AddSingleton<AuditInterceptor>();
        services.AddSingleton<OutboxInterceptor>();

        // Registers IDbContextFactory<CrmDbContext> (for handlers) and a scoped CrmDbContext
        // (for Identity stores and health checks).
        services.AddDbContextFactory<CrmDbContext>((sp, options) => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(
                sp.GetRequiredService<TimestampInterceptor>(),
                sp.GetRequiredService<AuditInterceptor>(),
                sp.GetRequiredService<OutboxInterceptor>()));

        services.AddSingleton<ICrmDbContextFactory, CrmDbContextFactory>();
        services.AddSingleton<IAppSettings, AppSettings>();

        services.AddWebhooks(configuration);

        return services;
    }

    /// <summary>
    /// Starts the outbox processor (ADR-008) unless <c>Outbox:Enabled</c> is <c>false</c>.
    /// Only the web host calls this; handler tests drive <see cref="OutboxProcessor"/> directly.
    /// </summary>
    public static IServiceCollection AddOutboxProcessing(this IServiceCollection services, IConfiguration configuration)
    {
        if (configuration.GetValue($"{OutboxOptions.SectionName}:{nameof(OutboxOptions.Enabled)}", true))
        {
            services.AddHostedService<OutboxBackgroundService>();
        }

        return services;
    }

    private static void AddWebhooks(this IServiceCollection services, IConfiguration configuration)
    {
        // Idempotent; the web host additionally sets the application name and the key directory (ADR-009).
        services.AddDataProtection();
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();

        // Webhooks:AllowedHttpHosts as a comma-separated value (environment variable) or as an array.
        var hostsSection = configuration.GetSection("Webhooks:AllowedHttpHosts");
        var allowedHttpHosts = hostsSection.Value?.Split(',') ?? hostsSection.Get<string[]>() ?? [];
        services.AddSingleton(new WebhookTargets(allowedHttpHosts));

        services.AddHttpClient(HttpWebhookSender.ClientName, client => client.Timeout = HttpWebhookSender.Timeout)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddSingleton<IWebhookSender, HttpWebhookSender>();

        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        services.AddSingleton<OutboxProcessor>();
    }

    /// <summary>
    /// Seeds the admin user from the "Admin" section on startup. Requires ASP.NET Core Identity to be registered.
    /// </summary>
    public static IServiceCollection AddAdminSeeding(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AdminOptions>(configuration.GetSection(AdminOptions.SectionName));
        services.AddHostedService<AdminUserSeeder>();

        return services;
    }
}
