using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SoloCrm.Web.Hosting;
using static SoloCrm.IntegrationTests.Web.CrmWebApplicationFactory;

namespace SoloCrm.IntegrationTests.Web;

[Trait("Category", "Integration")]
[Collection(WebHostTests.Name)]
public sealed partial class AuthorizationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private CrmWebApplicationFactory _factory = null!;

    public async ValueTask InitializeAsync()
    {
        var connectionString = await CreateDatabaseAsync(postgres, TestContext.Current.CancellationToken);
        _factory = new CrmWebApplicationFactory(connectionString, AdminEmail, AdminPassword);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Get_ProtectedPageAnonymous_RedirectsToLogin()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.AbsolutePath.Should().Be("/Account/Login");
    }

    [Theory]
    [InlineData("/Account/Login")]
    [InlineData("/Account/ForgotPassword")]
    [InlineData("/health/live")]
    [InlineData("/_framework/blazor.web.js")]
    public async Task Get_PublicEndpointAnonymous_ReturnsOk(string path)
    {
        var client = CreateClient();

        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void Endpoints_SelfRegistration_AreNotMapped()
    {
        var routes = _factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText?.TrimStart('/'))
            .ToList();

        routes.Should().Contain("Account/Login");
        routes.Should().NotContain(route => route != null && (
            route.StartsWith("Account/Register", StringComparison.OrdinalIgnoreCase) ||
            route.Contains("ExternalLogin", StringComparison.OrdinalIgnoreCase) ||
            route.StartsWith("Account/ResendEmailConfirmation", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Login_SeededAdmin_GrantsAccessToProtectedPage()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClient();

        var login = await PostLoginAsync(client, AdminPassword, ct);
        login.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var home = await client.GetAsync("/", ct);

        home.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_TooManyAttempts_ReturnsTooManyRequests()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = CreateClient();

        for (var i = 0; i < LoginRateLimiting.PermitLimit; i++)
        {
            var attempt = await PostLoginAsync(client, "wrong-password", ct);
            attempt.StatusCode.Should().Be(HttpStatusCode.OK, "attempt {0} is within the limit", i + 1);
        }

        var rejected = await PostLoginAsync(client, AdminPassword, ct);

        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.Should().NotBeNull();
        (await client.GetAsync("/Account/Login", ct)).StatusCode.Should().Be(HttpStatusCode.OK, "rendering the form is not limited");
    }

    private static async Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string password, CancellationToken ct)
    {
        var loginPage = await client.GetStringAsync("/Account/Login", ct);
        var token = AntiforgeryTokenRegex().Match(loginPage).Groups[1].Value;
        token.Should().NotBeEmpty();

        return await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["__RequestVerificationToken"] = token,
            ["Input.Email"] = AdminEmail,
            ["Input.Password"] = password,
        }), ct);
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""")]
    private static partial Regex AntiforgeryTokenRegex();
}
