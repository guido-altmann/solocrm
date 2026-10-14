using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SoloCrm.Application.Abstractions;
using SoloCrm.Web.Endpoints;
using static SoloCrm.IntegrationTests.Web.CrmWebApplicationFactory;

namespace SoloCrm.IntegrationTests.Web;

[Trait("Category", "Integration")]
[Collection(WebHostTests.Name)]
public sealed class MailConnectEndpointTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private readonly FakeConnector _connector = new();
    private CrmWebApplicationFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var connectionString = await CreateDatabaseAsync(postgres, Ct);
        _factory = new CrmWebApplicationFactory(connectionString, AdminEmail, AdminPassword);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Theory]
    [InlineData("/mail/connect")]
    [InlineData("/mail/callback?code=x&state=y")]
    public async Task Get_Anonymous_RedirectsToLogin(string path)
    {
        using var client = CreateClient();

        var response = await client.GetAsync(path, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.AbsolutePath.Should().Be("/Account/Login");
    }

    [Fact]
    public async Task Connect_SignedIn_RedirectsToMicrosoftWithStateAndPkceAndSetsProtectedCookie()
    {
        using var client = await SignedInClientAsync();

        var response = await client.GetAsync("/mail/connect", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.Host.Should().Be("login.example.test");
        _connector.RedirectUri.Should().Be("http://localhost/mail/callback");
        _connector.State.Should().NotBeNullOrEmpty();
        _connector.Challenge.Should().NotBeNullOrEmpty();
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(MailConnectEndpoints.CookieName, StringComparison.Ordinal));
        cookie.Should().Contain("httponly", Exactly.Once()).And.Contain("samesite=lax").And.Contain("path=/mail");
        cookie.Should().NotContain(_connector.State!, "the cookie is encrypted");
    }

    [Fact]
    public async Task Connect_NotConfigured_ReturnsToSettings()
    {
        _connector.IsConfigured = false;
        using var client = await SignedInClientAsync();

        var response = await client.GetAsync("/mail/connect", Ct);

        response.Headers.Location!.OriginalString.Should().Be("/settings?mail=notconfigured");
    }

    [Fact]
    public async Task Callback_MatchingState_ConnectsWithCodeAndVerifierAndClearsTheCookie()
    {
        using var client = await SignedInClientAsync();
        await client.GetAsync("/mail/connect", Ct);

        var response = await client.GetAsync($"/mail/callback?code=code-1&state={Uri.EscapeDataString(_connector.State!)}", Ct);

        response.Headers.Location!.OriginalString.Should().Be("/settings?mail=connected");
        _connector.Connected.Should().ContainSingle().Which.Code.Should().Be("code-1");
        var (_, verifier, redirectUri) = _connector.Connected.Single();
        redirectUri.Should().Be("http://localhost/mail/callback");
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).Should().Be(_connector.Challenge);
        response.Headers.GetValues("Set-Cookie").Should().Contain(c => c.StartsWith(MailConnectEndpoints.CookieName + "=;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Callback_StateDiffers_FailsWithoutConnecting()
    {
        using var client = await SignedInClientAsync();
        await client.GetAsync("/mail/connect", Ct);

        var response = await client.GetAsync("/mail/callback?code=code-1&state=forged", Ct);

        response.Headers.Location!.OriginalString.Should().Be("/settings?mail=failed");
        _connector.Connected.Should().BeEmpty();
    }

    [Fact]
    public async Task Callback_WithoutCookie_Expired()
    {
        using var client = await SignedInClientAsync();

        var response = await client.GetAsync("/mail/callback?code=code-1&state=anything", Ct);

        response.Headers.Location!.OriginalString.Should().Be("/settings?mail=expired");
        _connector.Connected.Should().BeEmpty();
    }

    [Fact]
    public async Task Callback_UserDeclined_ReturnsDenied()
    {
        using var client = await SignedInClientAsync();
        await client.GetAsync("/mail/connect", Ct);

        var response = await client.GetAsync($"/mail/callback?error=access_denied&state={Uri.EscapeDataString(_connector.State!)}", Ct);

        response.Headers.Location!.OriginalString.Should().Be("/settings?mail=denied");
        _connector.Connected.Should().BeEmpty();
    }

    [Fact]
    public async Task Callback_MicrosoftRejectsTheCode_ReturnsFailed()
    {
        _connector.ConnectError = new MailSourceException("Anmeldung abgelehnt.");
        using var client = await SignedInClientAsync();
        await client.GetAsync("/mail/connect", Ct);

        var response = await client.GetAsync($"/mail/callback?code=bad&state={Uri.EscapeDataString(_connector.State!)}", Ct);

        response.Headers.Location!.OriginalString.Should().Be("/settings?mail=failed");
    }

    [Fact]
    public async Task Callback_UsedTwice_SecondTimeIsExpired()
    {
        using var client = await SignedInClientAsync();
        await client.GetAsync("/mail/connect", Ct);
        var url = $"/mail/callback?code=code-1&state={Uri.EscapeDataString(_connector.State!)}";
        await client.GetAsync(url, Ct);

        var second = await client.GetAsync(url, Ct);

        second.Headers.Location!.OriginalString.Should().Be("/settings?mail=expired");
        _connector.Connected.Should().HaveCount(1);
    }

    [Fact]
    public async Task Settings_SignedIn_ShowsTheMailSection()
    {
        using var client = await SignedInClientAsync();

        var response = await client.GetAsync("/settings?mail=connected", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("data-section=\"mail\"");
    }

    private HttpClient CreateClient() => _factory
        .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IMailConnector>(_connector);
        }))
        .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = CreateClient();
        var login = await WebLogin.PostAsync(client, AdminEmail, AdminPassword, Ct);
        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        return client;
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class FakeConnector : IMailConnector
    {
        public bool IsConfigured { get; set; } = true;

        public MailSourceException? ConnectError { get; set; }

        public string? RedirectUri { get; private set; }

        public string? State { get; private set; }

        public string? Challenge { get; private set; }

        public List<(string Code, string Verifier, string RedirectUri)> Connected { get; } = [];

        public Uri BuildAuthorizationUri(string redirectUri, string state, string codeChallenge)
        {
            (RedirectUri, State, Challenge) = (redirectUri, state, codeChallenge);
            return new Uri($"https://login.example.test/authorize?state={HttpUtility.UrlEncode(state)}");
        }

        public Task<string> ConnectAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken)
        {
            if (ConnectError is not null)
            {
                throw ConnectError;
            }

            Connected.Add((code, codeVerifier, redirectUri));
            return Task.FromResult("guido@example.test");
        }

        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<MailConnectionInfo?> GetConnectionAsync(CancellationToken cancellationToken) => Task.FromResult<MailConnectionInfo?>(null);

        public Task RecordSyncAsync(string? error, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
