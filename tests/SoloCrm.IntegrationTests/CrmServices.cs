using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SoloCrm.Application;
using SoloCrm.Infrastructure;

namespace SoloCrm.IntegrationTests;

/// <summary>
/// Builds the production DI setup (Application + Infrastructure) for a test database.
/// </summary>
public static class CrmServices
{
    public static ServiceProvider Create(
        string connectionString, TimeProvider timeProvider, IReadOnlyDictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Crm"] = connectionString })
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(timeProvider);
        services.Configure<IdentityOptions>(options => options.Stores.SchemaVersion = IdentitySchemaVersions.Version3);
        services.AddApplication();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }
}
