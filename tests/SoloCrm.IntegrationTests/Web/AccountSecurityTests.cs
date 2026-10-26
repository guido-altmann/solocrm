using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog.Core;
using Serilog.Events;
using SoloCrm.Infrastructure.Identity;
using SoloCrm.Web.Hosting;
using static SoloCrm.IntegrationTests.Web.CrmWebApplicationFactory;

namespace SoloCrm.IntegrationTests.Web;

/// <summary>Sign-in hardening after the external audit (SOL-30): lockout, password rules, cookies, security events.</summary>
[Trait("Category", "Integration")]
[Collection(WebHostTests.Name)]
public sealed partial class AccountSecurityTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private readonly CollectingSink _logs = new();
    private CrmWebApplicationFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var connectionString = await CreateDatabaseAsync(postgres, Ct);
        _factory = new CrmWebApplicationFactory(connectionString, AdminEmail, AdminPassword);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Login_WrongPassword_CountsTowardsLockoutAndLogsSecurityEvent()
    {
        var factory = WithLogSink();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        await WebLogin.PostAsync(client, AdminEmail, "wrong-password", Ct);

        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = (await users.FindByEmailAsync(AdminEmail))!;
        (await users.GetLockoutEnabledAsync(admin)).Should().BeTrue("the seeded admin can be locked out");
        (await users.GetAccessFailedCountAsync(admin)).Should().Be(1);
        var warning = _logs.Events.Should().ContainSingle(e => e.MessageTemplate.Text == "Failed sign-in attempt.").Subject;
        warning.Level.Should().Be(LogEventLevel.Warning);
        warning.RenderMessage(System.Globalization.CultureInfo.InvariantCulture).Should().NotContain(AdminEmail);
    }

    [Fact]
    public async Task AccessFailed_MaxAttempts_LocksOutForAtMostFifteenMinutes()
    {
        CreateClient(); // starts the host, which seeds the admin
        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = (await users.FindByEmailAsync(AdminEmail))!;

        for (var i = 1; i < AccountSecurity.MaxFailedAccessAttempts; i++)
        {
            await users.AccessFailedAsync(admin);
        }

        (await users.IsLockedOutAsync(admin)).Should().BeFalse("one attempt is left");
        await users.AccessFailedAsync(admin);

        (await users.IsLockedOutAsync(admin)).Should().BeTrue();
        (await users.GetLockoutEndDateAsync(admin)).Should().BeCloseTo(DateTimeOffset.UtcNow + TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Login_LockedOutAccount_RedirectsToGermanLockoutPageEvenWithCorrectPassword()
    {
        await LockOutAdminAsync();
        var client = CreateClient();

        var login = await WebLogin.PostAsync(client, AdminEmail, AdminPassword, Ct);

        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        login.Headers.Location!.OriginalString.Should().Contain("Account/Lockout");
        (await client.GetStringAsync("/Account/Lockout", Ct)).Should().Contain("für 15 Minuten gesperrt");
        (await client.GetAsync("/", Ct)).StatusCode.Should().Be(HttpStatusCode.Redirect, "the lockout prevents the sign-in");
    }

    [Fact]
    public async Task Login_WrongAuthenticatorAndRecoveryCode_CountTowardsLockout()
    {
        await EnableTwoFactorAsync();
        var client = CreateClient();
        var login = await WebLogin.PostAsync(client, AdminEmail, AdminPassword, Ct);

        await PostFormAsync(client, login.Headers.Location!.OriginalString, "login-with-2fa", new() { ["Input.TwoFactorCode"] = "000000" });
        var afterAuthenticator = await AccessFailedCountAsync();
        await PostFormAsync(client, "/Account/LoginWithRecoveryCode", "login-with-recovery-code", new() { ["Input.RecoveryCode"] = "wrong-code" });
        var afterRecovery = await AccessFailedCountAsync();

        afterAuthenticator.Should().Be(1);
        afterRecovery.Should().Be(2, "Identity does not count recovery codes itself; the page does");
    }

    [Fact]
    public async Task Login_ValidRecoveryCodeWhileLockedOut_RedirectsToLockoutPage()
    {
        var codes = await EnableTwoFactorAsync();
        var client = CreateClient();
        await WebLogin.PostAsync(client, AdminEmail, AdminPassword, Ct);
        await LockOutAdminAsync();

        var response = await PostFormAsync(client, "/Account/LoginWithRecoveryCode", "login-with-recovery-code", new() { ["Input.RecoveryCode"] = codes[0] });

        response.Headers.Location!.OriginalString.Should().Contain("Account/Lockout");
        (await client.GetAsync("/", Ct)).StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Theory]
    [InlineData("kurz-1234", false)]
    [InlineData("aaaaaaaaaaaaaaa", false)]
    [InlineData("lange passphrase ohne sonderzeichen", true)]
    public async Task PasswordValidation_LengthInsteadOfCharacterClasses(string password, bool valid)
    {
        CreateClient();
        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = (await users.FindByEmailAsync(AdminEmail))!;

        var results = await Task.WhenAll(users.PasswordValidators.Select(v => v.ValidateAsync(users, admin, password)));

        results.All(r => r.Succeeded).Should().Be(valid);
    }

    [Fact]
    public async Task PasskeyRequestOptions_TooManyRequests_AreRateLimitedLikeTheLogin()
    {
        var client = CreateClient();
        var token = AntiforgeryTokenRegex().Match(await client.GetStringAsync("/Account/Login", Ct)).Groups[1].Value;

        HttpResponseMessage? last = null;
        for (var i = 0; i <= LoginRateLimiting.PermitLimit; i++)
        {
            last = await client.PostAsync(
                $"/Account/PasskeyRequestOptions?username={Uri.EscapeDataString(AdminEmail)}",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }),
                Ct);
        }

        last!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task LoginRateLimit_Rejection_LogsSecurityEvent()
    {
        var factory = WithLogSink();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        for (var i = 0; i <= LoginRateLimiting.PermitLimit; i++)
        {
            await WebLogin.PostAsync(client, AdminEmail, "wrong-password", Ct);
        }

        _logs.Events.Should().Contain(e => e.MessageTemplate.Text == "Sign-in rate limit exceeded" && e.Level == LogEventLevel.Warning);
    }

    [Fact]
    public async Task SignInCookie_Development_IsHttpOnlyLaxAndFollowsRequestScheme()
    {
        var client = CreateClient();

        var login = await WebLogin.PostAsync(client, AdminEmail, AdminPassword, Ct);

        var cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal));
        cookie.Should().Contain("httponly").And.Contain("samesite=lax").And.NotContain("secure", "local development runs on plain http");
        var options = _factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        options.ExpireTimeSpan.Should().Be(TimeSpan.FromDays(14));
        options.SlidingExpiration.Should().BeTrue();
    }

    [Fact]
    public void IdentityCookies_Production_AreAlwaysSecure()
    {
        using var production = _factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        var options = production.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();

        foreach (var scheme in new[] { IdentityConstants.ApplicationScheme, IdentityConstants.ExternalScheme, IdentityConstants.TwoFactorUserIdScheme, IdentityConstants.TwoFactorRememberMeScheme })
        {
            var cookie = options.Get(scheme).Cookie;
            cookie.SecurePolicy.Should().Be(CookieSecurePolicy.Always, scheme);
            cookie.HttpOnly.Should().BeTrue(scheme);
            cookie.SameSite.Should().Be(SameSiteMode.Lax, scheme);
        }
    }

    private WebApplicationFactory<Program> WithLogSink() =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<ILogEventSink>(_logs)));

    private async Task<int> AccessFailedCountAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.GetAccessFailedCountAsync((await users.FindByEmailAsync(AdminEmail))!);
    }

    private async Task LockOutAdminAsync()
    {
        CreateClient();
        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = (await users.FindByEmailAsync(AdminEmail))!;
        await users.SetLockoutEndDateAsync(admin, DateTimeOffset.UtcNow + AccountSecurity.LockoutDuration);
    }

    /// <summary>Enables TOTP for the admin like the setup page does; returns the recovery codes.</summary>
    private async Task<IReadOnlyList<string>> EnableTwoFactorAsync()
    {
        CreateClient();
        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = (await users.FindByEmailAsync(AdminEmail))!;
        await users.ResetAuthenticatorKeyAsync(admin);
        await users.SetTwoFactorEnabledAsync(admin, true);
        return [.. (await users.GenerateNewTwoFactorRecoveryCodesAsync(admin, 10))!];
    }

    /// <summary>Posts a statically rendered form with the antiforgery token of the page.</summary>
    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, string formName, Dictionary<string, string> fields)
    {
        var page = await client.GetStringAsync(path, Ct);
        fields["_handler"] = formName;
        fields["__RequestVerificationToken"] = AntiforgeryTokenRegex().Match(page).Groups[1].Value;
        return await client.PostAsync(path, new FormUrlEncodedContent(fields), Ct);
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

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
