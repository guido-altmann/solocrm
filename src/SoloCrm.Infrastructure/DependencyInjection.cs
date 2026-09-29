using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SoloCrm.Application.Abstractions;
using SoloCrm.Infrastructure.Identity;
using SoloCrm.Infrastructure.Persistence;
using SoloCrm.Infrastructure.Persistence.Interceptors;
using SoloCrm.Infrastructure.Settings;

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

        return services;
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
