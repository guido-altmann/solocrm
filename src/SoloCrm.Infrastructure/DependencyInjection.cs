using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Enrichment;
using SoloCrm.Application.Features.Mail;
using SoloCrm.Application.Features.Nextcloud;
using SoloCrm.Application.Features.Webhooks;
using SoloCrm.Infrastructure.Enrichment;
using SoloCrm.Infrastructure.Identity;
using SoloCrm.Infrastructure.Mail;
using SoloCrm.Infrastructure.Nextcloud;
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
        services.Configure<MailSyncOptions>(configuration.GetSection(MailSyncOptions.SectionName));
        services.AddTransient(sp => sp.GetRequiredService<IOptions<MailSyncOptions>>().Value);
        services.AddMailSource(configuration);
        services.AddEnrichmentAgent(configuration);
        services.AddSingleton<IAppSettings, AppSettings>();

        services.AddWebhooks(configuration);
        services.AddNextcloud(configuration);

        return services;
    }

    /// <summary>
    /// Starts the enrichment processor (ADR-016) if the agent is configured and <c>Enrichment:Enabled</c> is not
    /// <c>false</c>. Only the web host calls this; tests drive <see cref="EnrichmentProcessor"/> directly.
    /// </summary>
    public static IServiceCollection AddEnrichmentProcessing(this IServiceCollection services, IConfiguration configuration)
    {
        var enabled = configuration.GetValue($"{EnrichmentProcessingOptions.SectionName}:{nameof(EnrichmentProcessingOptions.Enabled)}", true);
        if (enabled && (configuration.GetSection(AiOptions.SectionName).Get<AiOptions>()?.IsConfigured ?? false))
        {
            services.AddSingleton<EnrichmentProcessor>();
            services.AddHostedService<EnrichmentBackgroundService>();
        }

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

    /// <summary>
    /// The Foundry agent for data enrichment (ADR-016) if <c>Ai</c> is set up: registers the agent-scoped client, which signs in
    /// with <c>DefaultAzureCredential</c>. Without configuration no client exists and <see cref="IEnrichmentAvailability"/> is
    /// <c>false</c>; the host still starts. Creating the client makes no network call.
    /// </summary>
    private static void AddEnrichmentAgent(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();
        services.AddSingleton<IEnrichmentAvailability>(new EnrichmentAvailability(options.IsConfigured));
        services.Configure<EnrichmentOptions>(configuration.GetSection(EnrichmentOptions.SectionName));
        services.Configure<EnrichmentProcessingOptions>(configuration.GetSection(EnrichmentProcessingOptions.SectionName));
        services.AddTransient(sp => sp.GetRequiredService<IOptions<EnrichmentOptions>>().Value);

        if (!options.IsConfigured)
        {
            services.AddSingleton<IEnrichmentProvider, NotConfiguredEnrichmentProvider>();
            return;
        }

        services.AddSingleton<IFoundryAgent>(sp => new FoundryAgent(sp.GetRequiredService<ProjectResponsesClient>()));
        services.AddSingleton<IEnrichmentProvider, FoundryEnrichmentProvider>();
        services.AddSingleton(_ =>
        {
            var project = new AIProjectClient(new Uri(options.ProjectEndpoint!), new DefaultAzureCredential());
            return project.ProjectOpenAIClient.GetProjectResponsesClientForAgentEndpoint(options.AgentName!);
        });
    }

    /// <summary>
    /// Microsoft 365 mailbox access (ADR-013) if <c>Mail:Graph</c> is set up; otherwise sync and connecting report that
    /// no mailbox is connected.
    /// </summary>
    private static void AddMailSource(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GraphMailOptions>(configuration.GetSection(GraphMailOptions.SectionName));
        services.Configure<MailWorkerOptions>(configuration.GetSection(MailWorkerOptions.SectionName));

        if (configuration.GetSection(GraphMailOptions.SectionName).Get<GraphMailOptions>()?.IsConfigured == true)
        {
            services.AddHttpClient(GraphAuthenticator.ClientName, client => client.Timeout = TimeSpan.FromSeconds(30))
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
            services.AddSingleton<GraphAuthenticator>();
            services.AddSingleton<IMailConnector>(sp => sp.GetRequiredService<GraphAuthenticator>());
            services.AddSingleton<IGraphAccessTokenProvider>(sp => sp.GetRequiredService<GraphAuthenticator>());
            services.AddSingleton<IMailSource, GraphMailSource>();
        }
        else
        {
            services.AddSingleton<IMailConnector, NotConfiguredMailConnector>();
            services.AddSingleton<IMailSource, NotConnectedMailSource>();
        }
    }

    /// <summary>Starts the mail sync (ADR-013) unless <c>Mail:Enabled</c> is <c>false</c>; only the web host calls this.</summary>
    public static IServiceCollection AddMailSync(this IServiceCollection services, IConfiguration configuration)
    {
        if (configuration.GetValue($"{MailWorkerOptions.SectionName}:{nameof(MailWorkerOptions.Enabled)}", true))
        {
            services.AddHostedService<MailSyncBackgroundService>();
        }

        return services;
    }

    /// <summary>Starts the Nextcloud sync (ADR-014) unless <c>Nextcloud:Enabled</c> is <c>false</c>; only the web host calls this.</summary>
    public static IServiceCollection AddNextcloudSync(this IServiceCollection services, IConfiguration configuration)
    {
        if (configuration.GetValue($"{NextcloudWorkerOptions.SectionName}:{nameof(NextcloudWorkerOptions.Enabled)}", true))
        {
            services.AddHostedService<NextcloudSyncBackgroundService>();
        }

        return services;
    }

    /// <summary>CardDAV/CalDAV access to Nextcloud (ADR-014); the account is entered in the settings.</summary>
    private static void AddNextcloud(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<NextcloudWorkerOptions>(configuration.GetSection(NextcloudWorkerOptions.SectionName));

        // Nextcloud:AllowedHttpHosts as a comma-separated value (environment variable) or as an array.
        var hostsSection = configuration.GetSection("Nextcloud:AllowedHttpHosts");
        var allowedHttpHosts = hostsSection.Value?.Split(',') ?? hostsSection.Get<string[]>() ?? [];
        services.AddSingleton(new NextcloudServers(allowedHttpHosts));
        services.AddSingleton(new NextcloudSyncOptions { AppBaseUrl = configuration["App:BaseUrl"] });

        // Redirects are followed by the client itself, only within the origin of the server (credentials!).
        services.AddHttpClient(NextcloudDavClient.ClientName, client => client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddSingleton<IDavSyncTarget, NextcloudDavClient>();
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
