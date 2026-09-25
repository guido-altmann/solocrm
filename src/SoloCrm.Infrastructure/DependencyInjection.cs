using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SoloCrm.Application.Abstractions;
using SoloCrm.Infrastructure.Identity;
using SoloCrm.Infrastructure.Persistence;
using SoloCrm.Infrastructure.Persistence.Interceptors;

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
        services.AddSingleton<TimestampInterceptor>();

        // Registers IDbContextFactory<CrmDbContext> (for handlers) and a scoped CrmDbContext
        // (for Identity stores and health checks).
        services.AddDbContextFactory<CrmDbContext>((sp, options) => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(sp.GetRequiredService<TimestampInterceptor>()));

        services.AddSingleton<ICrmDbContextFactory, CrmDbContextFactory>();

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
