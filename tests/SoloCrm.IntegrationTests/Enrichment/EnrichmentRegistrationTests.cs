using Azure.AI.Extensions.OpenAI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SoloCrm.Application.Abstractions;
using SoloCrm.Infrastructure.Enrichment;

namespace SoloCrm.IntegrationTests.Enrichment;

/// <summary>
/// Registration of the Foundry agent client (ADR-016, point 6). Needs no database: the connection string is never used
/// and creating the client makes no network call.
/// </summary>
public sealed class EnrichmentRegistrationTests
{
    private static readonly Dictionary<string, string?> Complete = new()
    {
        ["Ai:ProjectEndpoint"] = "https://foundry.example.test/api/projects/crm",
        ["Ai:AgentName"] = "company-enrichment",
    };

    private static ServiceProvider Build(Dictionary<string, string?> settings) =>
        CrmServices.Create("Host=localhost;Database=unused", new FakeTimeProvider(), settings);

    [Fact]
    public void Services_NothingConfigured_StartsWithoutClientAndIsUnavailable()
    {
        using var services = Build([]);

        services.GetRequiredService<IEnrichmentAvailability>().IsAvailable.Should().BeFalse();
        services.GetService<ProjectResponsesClient>().Should().BeNull();
        services.GetRequiredService<IEnrichmentProvider>().Should().BeOfType<NotConfiguredEnrichmentProvider>();
    }

    [Fact]
    public void Services_Configured_UsesTheFoundryProvider()
    {
        using var services = Build(Complete);

        services.GetRequiredService<IEnrichmentProvider>().Should().BeOfType<FoundryEnrichmentProvider>()
            .Which.Name.Should().Be("FoundryAgent");
    }

    [Fact]
    public void Services_Configured_IsAvailableAndResolvesTheAgentClient()
    {
        using var services = Build(Complete);

        services.GetRequiredService<IEnrichmentAvailability>().IsAvailable.Should().BeTrue();
        services.GetRequiredService<ProjectResponsesClient>().Should().NotBeNull();
        services.GetRequiredService<ProjectResponsesClient>().Should().BeSameAs(services.GetRequiredService<ProjectResponsesClient>());
    }

    [Theory]
    [InlineData("Ai:ProjectEndpoint")]
    [InlineData("Ai:AgentName")]
    public void Services_OneValueMissing_IsUnavailableWithoutClient(string missing)
    {
        var settings = new Dictionary<string, string?>(Complete) { [missing] = " " };

        using var services = Build(settings);

        services.GetRequiredService<IEnrichmentAvailability>().IsAvailable.Should().BeFalse();
        services.GetService<ProjectResponsesClient>().Should().BeNull();
    }

    [Theory]
    [InlineData("foundry.example.test/api/projects/crm")]
    [InlineData("http://foundry.example.test/api/projects/crm")]
    [InlineData("not a url")]
    public void Services_EndpointIsNotAnAbsoluteHttpsUrl_IsUnavailable(string endpoint)
    {
        var settings = new Dictionary<string, string?>(Complete) { ["Ai:ProjectEndpoint"] = endpoint };

        using var services = Build(settings);

        services.GetRequiredService<IEnrichmentAvailability>().IsAvailable.Should().BeFalse();
        services.GetService<ProjectResponsesClient>().Should().BeNull();
    }
}
