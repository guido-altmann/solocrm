using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static SoloCrm.IntegrationTests.Web.CrmWebApplicationFactory;

namespace SoloCrm.IntegrationTests.Web;

/// <summary>Which proxies the app trusts for X-Forwarded-* (SOL-34, audit F-02).</summary>
[Trait("Category", "Integration")]
[Collection(WebHostTests.Name)]
public sealed class ReverseProxyTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private CrmWebApplicationFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var connectionString = await CreateDatabaseAsync(postgres, Ct);
        _factory = new CrmWebApplicationFactory(connectionString, AdminEmail, AdminPassword);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public void ForwardedHeaders_NoConfiguration_TrustsPrivateNetworksForOneHop()
    {
        var options = _factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        options.ForwardLimit.Should().Be(1);
        options.KnownIPNetworks.Should().Contain([IPNetwork.Parse("10.0.0.0/8"), IPNetwork.Parse("172.16.0.0/12"), IPNetwork.Parse("192.168.0.0/16")]);
    }

    [Fact]
    public void ForwardedHeaders_CommaSeparatedNetworks_ReplaceTheDefaults()
    {
        using var factory = _factory.WithWebHostBuilder(builder => builder.UseSetting("ForwardedHeaders:KnownNetworks", "10.0.1.0/24, 10.0.2.0/24"));

        var options = factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        options.KnownIPNetworks.Should().Contain([IPNetwork.Parse("10.0.1.0/24"), IPNetwork.Parse("10.0.2.0/24")]);
        options.KnownIPNetworks.Should().NotContain(IPNetwork.Parse("10.0.0.0/8"));
    }

    [Fact]
    public void ForwardedHeaders_OnlyKnownProxy_TrustsNoPrivateNetwork()
    {
        using var factory = _factory.WithWebHostBuilder(builder => builder.UseSetting("ForwardedHeaders:KnownProxies", "10.0.1.5"));

        var options = factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        options.KnownProxies.Should().Contain(IPAddress.Parse("10.0.1.5"));
        options.KnownIPNetworks.Should().NotContain(IPNetwork.Parse("10.0.0.0/8"));
    }

    [Fact]
    public void ForwardedHeaders_AllowedHostsSet_RestrictsForwardedHost()
    {
        using var factory = _factory.WithWebHostBuilder(builder => builder.UseSetting("AllowedHosts", "crm.example.de"));

        var options = factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        options.AllowedHosts.Should().Equal("crm.example.de");
    }

    [Fact]
    public void ForwardedHeaders_AllHostsAllowed_AcceptsAnyForwardedHost()
    {
        var options = _factory.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        options.AllowedHosts.Should().BeEmpty();
    }
}
