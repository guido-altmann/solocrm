using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Serilog.Core;
using Serilog.Events;
using SoloCrm.Infrastructure.Identity;
using SoloCrm.Web.Components.Account;
using static SoloCrm.IntegrationTests.Web.CrmWebApplicationFactory;

namespace SoloCrm.IntegrationTests.Web;

/// <summary>Changing sign-in factors requires a recently confirmed password (SOL-34, audit F-03).</summary>
[Trait("Category", "Integration")]
[Collection(WebHostTests.Name)]
public sealed partial class FactorChangeTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private const string ResetAuthenticatorPath = "/Account/Manage/ResetAuthenticator";

    private readonly CollectingSink _logs = new();
    // Starts at the real time: the client's cookie container drops cookies by the real clock.
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private CrmWebApplicationFactory _factory = null!;
    private WebApplicationFactory<Program> _host = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var connectionString = await CreateDatabaseAsync(postgres, Ct);
        _factory = new CrmWebApplicationFactory(connectionString, AdminEmail, AdminPassword);
        _host = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILogEventSink>(_logs);
            services.AddSingleton<TimeProvider>(_time);
        }));
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        await _factory.DisposeAsync();
    }

    [Theory]
    [InlineData("/Account/Manage/Passkeys")]
    [InlineData("/Account/Manage/EnableAuthenticator")]
    [InlineData("/Account/Manage/Disable2fa")]
    [InlineData("/Account/Manage/GenerateRecoveryCodes")]
    [InlineData(ResetAuthenticatorPath)]
    public async Task Get_FactorPageWithoutConfirmation_RedirectsToConfirmPassword(string path)
    {
        var client = await SignedInClientAsync();

        var response = await client.GetAsync(path, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain(FactorChanges.ConfirmPath)
            .And.Contain($"ReturnUrl={Uri.EscapeDataString(path.TrimStart('/'))}");
    }

    [Fact]
    public async Task Post_ResetAuthenticatorWithoutConfirmation_ChangesNothing()
    {
        var client = await SignedInClientAsync();
        var keyBefore = await AuthenticatorKeyAsync();

        var response = await PostAsync(client, ResetAuthenticatorPath, "reset-authenticator", [], await TokenAsync(client, "/Account/Manage/ChangePassword"));

        response.Headers.Location!.OriginalString.Should().Contain(FactorChanges.ConfirmPath);
        (await AuthenticatorKeyAsync()).Should().Be(keyBefore);
    }

    [Fact]
    public async Task ConfirmPassword_WrongPassword_ShowsErrorAndCountsTowardsLockout()
    {
        var client = await SignedInClientAsync();

        var response = await ConfirmAsync(client, "wrong-password", ResetAuthenticatorPath.TrimStart('/'));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("Das Passwort ist falsch.");
        (response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : []).Should().NotContain(c => c.StartsWith(FactorChanges.CookieName, StringComparison.Ordinal));
        await using var scope = _host.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await users.GetAccessFailedCountAsync((await users.FindByEmailAsync(AdminEmail))!)).Should().Be(1);
        _logs.Events.Should().Contain(e => e.MessageTemplate.Text == "Failed password confirmation." && e.Level == LogEventLevel.Warning);
    }

    [Fact]
    public async Task ConfirmPassword_CorrectPassword_AllowsTheChangeAndRenewsTheSecurityStamp()
    {
        var client = await SignedInClientAsync();
        var keyBefore = await AuthenticatorKeyAsync();
        var stampBefore = await SecurityStampAsync();

        var confirm = await ConfirmAsync(client, AdminPassword, ResetAuthenticatorPath.TrimStart('/'));
        var reset = await PostAsync(client, ResetAuthenticatorPath, "reset-authenticator", [], await TokenAsync(client, ResetAuthenticatorPath));

        confirm.StatusCode.Should().Be(HttpStatusCode.Redirect);
        confirm.Headers.Location!.OriginalString.Should().EndWith(ResetAuthenticatorPath.TrimStart('/'));
        confirm.Headers.GetValues("Set-Cookie").Should().Contain(c => c.StartsWith(FactorChanges.CookieName + "=", StringComparison.Ordinal)
            && c.Contains("httponly", StringComparison.OrdinalIgnoreCase) && c.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
        reset.Headers.Location!.OriginalString.Should().Contain("Account/Manage/EnableAuthenticator");
        (await AuthenticatorKeyAsync()).Should().NotBe(keyBefore);
        (await SecurityStampAsync()).Should().NotBe(stampBefore);
        _logs.Events.Should().Contain(e => e.MessageTemplate.Text.StartsWith("Sign-in factor changed", StringComparison.Ordinal)
            && e.Level == LogEventLevel.Warning
            && !e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture).Contains(AdminEmail, StringComparison.Ordinal));
        (await client.GetAsync("/Account/Manage", Ct)).StatusCode.Should().Be(HttpStatusCode.OK, "the own session stays signed in");
    }

    [Fact]
    public async Task Confirmation_AfterTheWindow_IsAskedAgain()
    {
        var client = await SignedInClientAsync();
        await ConfirmAsync(client, AdminPassword, ResetAuthenticatorPath.TrimStart('/'));
        (await client.GetAsync(ResetAuthenticatorPath, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        _time.Advance(FactorChanges.Window + TimeSpan.FromSeconds(1));
        var response = await client.GetAsync(ResetAuthenticatorPath, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain(FactorChanges.ConfirmPath);
    }

    [Theory]
    [InlineData("https://evil.example/Account/Manage/Passkeys")]
    [InlineData("//evil.example/Account/Manage/Passkeys")]
    [InlineData("Account/Manage//evil.example")]
    [InlineData("contacts")]
    public async Task ConfirmPassword_ForeignReturnUrl_RedirectsToAccountPage(string returnUrl)
    {
        var client = await SignedInClientAsync();

        var response = await ConfirmAsync(client, AdminPassword, returnUrl);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().EndWith("Account/Manage").And.NotContain("evil");
    }

    [Fact]
    public async Task Logout_ClearsTheConfirmation()
    {
        var client = await SignedInClientAsync();
        await ConfirmAsync(client, AdminPassword, "Account/Manage/Passkeys");

        var logout = await PostAsync(client, "/Account/Logout", formName: null, new() { ["returnUrl"] = "" }, await TokenAsync(client, "/Account/Manage/ChangePassword"));

        logout.Headers.GetValues("Set-Cookie").Should().Contain(c => c.StartsWith(FactorChanges.CookieName + "=;", StringComparison.Ordinal));
    }

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        (await WebLogin.PostAsync(client, AdminEmail, AdminPassword, Ct)).StatusCode.Should().Be(HttpStatusCode.Redirect);
        return client;
    }

    private static async Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string password, string returnUrl)
    {
        var path = $"/{FactorChanges.ConfirmPath}?ReturnUrl={Uri.EscapeDataString(returnUrl)}";
        return await PostAsync(client, path, "confirm-password", new() { ["Input.Password"] = password }, await TokenAsync(client, path));
    }

    private static async Task<string> TokenAsync(HttpClient client, string path)
    {
        var page = await client.GetStringAsync(path, Ct);
        var token = AntiforgeryTokenRegex().Match(page).Groups[1].Value;
        token.Should().NotBeEmpty();
        return token;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string? formName, Dictionary<string, string> fields, string token)
    {
        if (formName is not null)
        {
            fields["_handler"] = formName;
        }

        fields["__RequestVerificationToken"] = token;
        return client.PostAsync(path, new FormUrlEncodedContent(fields), Ct);
    }

    private async Task<string?> AuthenticatorKeyAsync()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.GetAuthenticatorKeyAsync((await users.FindByEmailAsync(AdminEmail))!);
    }

    private async Task<string> SecurityStampAsync()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.GetSecurityStampAsync((await users.FindByEmailAsync(AdminEmail))!);
    }

    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""")]
    private static partial Regex AntiforgeryTokenRegex();

    /// <summary>Picked up by Serilog through <c>ReadFrom.Services</c> in Program.cs.</summary>
    private sealed class CollectingSink : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _events = new();

        public IReadOnlyCollection<LogEvent> Events => _events;

        public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
    }
}
