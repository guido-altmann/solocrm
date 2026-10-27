using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using SoloCrm.Web.Endpoints;
using static SoloCrm.IntegrationTests.Web.CrmWebApplicationFactory;

namespace SoloCrm.IntegrationTests.Web;

/// <summary>Security headers after the external audit (SOL-32, F-08).</summary>
[Trait("Category", "Integration")]
[Collection(WebHostTests.Name)]
public sealed partial class SecurityHeadersTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
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
    public async Task Page_Anonymous_SendsSecurityHeaders()
    {
        var response = await CreateClient().GetAsync("/Account/Login", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Header(response, "X-Content-Type-Options").Should().Be("nosniff");
        Header(response, "Referrer-Policy").Should().Be("no-referrer");
        Header(response, "Permissions-Policy").Should().Contain("camera=()").And.NotContain("publickey");
        Header(response, "Content-Security-Policy").Should()
            .Contain("default-src 'self'")
            .And.Contain("img-src 'self' data:")
            .And.Contain("frame-ancestors 'none'")
            .And.Contain("object-src 'none'")
            .And.NotContain("script-src 'self' 'unsafe-inline'");
    }

    [Fact]
    public async Task Page_ImportMap_CarriesNonceOfPolicy()
    {
        var client = CreateClient();

        var first = await client.GetAsync("/Account/Login", Ct);
        var second = await client.GetAsync("/Account/Login", Ct);

        var nonce = NonceRegex().Match(Header(first, "Content-Security-Policy")).Groups[1].Value;
        nonce.Should().NotBeEmpty();
        var html = await first.Content.ReadAsStringAsync(Ct);
        html.Should().MatchRegex($"<script[^>]*type=\"importmap\"[^>]*nonce=\"{Regex.Escape(nonce)}\"|<script[^>]*nonce=\"{Regex.Escape(nonce)}\"[^>]*type=\"importmap\"");
        Header(second, "Content-Security-Policy").Should().NotContain(nonce, "the nonce is new for every request");
    }

    [Fact]
    public async Task Api_Unauthorized_SendsSecurityHeaders()
    {
        var response = await CreateClient().GetAsync($"{ApiEndpoints.BasePath}/contacts", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        Header(response, "X-Content-Type-Options").Should().Be("nosniff");
        Header(response, "Content-Security-Policy").Should().Contain("frame-ancestors 'none'");
    }

    [Fact]
    public async Task Scalar_SignedIn_AllowsInlineScripts()
    {
        var client = CreateClient();
        await WebLogin.PostAsync(client, AdminEmail, AdminPassword, Ct);

        var response = await client.GetAsync("/scalar/", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Header(response, "Content-Security-Policy").Should()
            .Contain("script-src 'self' 'unsafe-inline'")
            .And.Contain("frame-ancestors 'none'");
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(", ", values) : "";

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [GeneratedRegex("'nonce-([^']+)'")]
    private static partial Regex NonceRegex();
}
