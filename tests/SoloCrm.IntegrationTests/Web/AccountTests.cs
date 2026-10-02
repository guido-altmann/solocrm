using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SoloCrm.Infrastructure.Identity;
using SoloCrm.Web.Hosting;
using static SoloCrm.IntegrationTests.Web.CrmWebApplicationFactory;

namespace SoloCrm.IntegrationTests.Web;

/// <summary>Account pages and the login with two factors (US-21 AK2, iteration 6 decision 8).</summary>
[Trait("Category", "Integration")]
[Collection(WebHostTests.Name)]
public sealed partial class AccountTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private CrmWebApplicationFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        var connectionString = await CreateDatabaseAsync(postgres, Ct);
        _factory = new CrmWebApplicationFactory(connectionString, AdminEmail, AdminPassword);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Theory]
    [InlineData("/Account/Manage")]
    [InlineData("/Account/Manage/ChangePassword")]
    [InlineData("/Account/Manage/TwoFactorAuthentication")]
    [InlineData("/Account/Manage/EnableAuthenticator")]
    [InlineData("/Account/Manage/GenerateRecoveryCodes")]
    [InlineData("/Account/Manage/ResetAuthenticator")]
    [InlineData("/Account/Manage/Disable2fa")]
    [InlineData("/Account/Manage/Passkeys")]
    public async Task Get_AccountPageAnonymous_RedirectsToLogin(string path)
    {
        var response = await CreateClient().GetAsync(path, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.AbsolutePath.Should().Be("/Account/Login");
    }

    [Theory]
    [InlineData("/Account/Manage/Email")]
    [InlineData("/Account/Manage/PersonalData")]
    [InlineData("/Account/Manage/DeletePersonalData")]
    [InlineData("/Account/Manage/SetPassword")]
    [InlineData("/Account/ForgotPassword")]
    [InlineData("/Account/ResetPassword")]
    [InlineData("/Account/ConfirmEmail")]
    public async Task Get_RemovedTemplatePageSignedIn_ReturnsNotFound(string path)
    {
        var client = await SignedInClientAsync();

        var response = await client.GetAsync(path, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_DownloadPersonalData_IsNotMapped()
    {
        var client = await SignedInClientAsync();

        var response = await client.PostAsync("/Account/Manage/DownloadPersonalData", new FormUrlEncodedContent([]), Ct);

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition.Should().BeNull();
    }

    [Fact]
    public async Task AccountPages_SignedIn_AreGermanWithoutBootstrap()
    {
        var client = await SignedInClientAsync();

        var overview = await client.GetStringAsync("/Account/Manage", Ct);
        var login = await CreateClient().GetStringAsync("/Account/Login", Ct);

        overview.Should().Contain("Angemeldet als").And.Contain(AdminEmail).And.Contain("Zwei-Faktor").And.NotContain("bootstrap");
        login.Should().Contain("Anmelden").And.Contain("Mit Passkey anmelden").And.NotContain("Log in");
    }

    [Fact]
    public async Task ChangePassword_ValidForm_ChangesPasswordAndKeepsSession()
    {
        var client = await SignedInClientAsync();

        var response = await PostFormAsync(client, "/Account/Manage/ChangePassword", "change-password", new()
        {
            ["Input.OldPassword"] = AdminPassword,
            ["Input.NewPassword"] = "Changed-Passw0rd!",
            ["Input.ConfirmPassword"] = "Changed-Passw0rd!",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await client.GetAsync("/", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var fresh = CreateClient();
        (await WebLogin.PostAsync(fresh, AdminEmail, "Changed-Passw0rd!", Ct)).StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_ShowsGermanError()
    {
        var client = await SignedInClientAsync();

        var response = await PostFormAsync(client, "/Account/Manage/ChangePassword", "change-password", new()
        {
            ["Input.OldPassword"] = "wrong",
            ["Input.NewPassword"] = "Changed-Passw0rd!",
            ["Input.ConfirmPassword"] = "Changed-Passw0rd!",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("Das aktuelle Passwort ist falsch.");
    }

    [Fact]
    public async Task EnableAuthenticator_SignedIn_ShowsQrCodeWithOtpAuthUriAndKey()
    {
        var client = await SignedInClientAsync();

        var page = await client.GetStringAsync("/Account/Manage/EnableAuthenticator", Ct);

        var uri = WebUtility.HtmlDecode(DataUriRegex().Match(page).Groups[1].Value);
        var key = KeyRegex().Match(page).Groups[1].Value.Replace(" ", "", StringComparison.Ordinal);
        uri.Should().StartWith("otpauth://totp/SoloCRM:admin%40example.test?secret=").And.EndWith("&issuer=SoloCRM&digits=6");
        uri.Should().Contain($"secret={key.ToUpperInvariant()}");
        page.Should().Contain("<svg");
    }

    [Fact]
    public async Task EnableAuthenticator_ValidCode_EnablesTwoFactorAndShowsRecoveryCodes()
    {
        var client = await SignedInClientAsync();
        var page = await client.GetStringAsync("/Account/Manage/EnableAuthenticator", Ct);
        var key = KeyRegex().Match(page).Groups[1].Value;

        var response = await PostFormAsync(client, "/Account/Manage/EnableAuthenticator", "send-code", new() { ["Input.Code"] = Totp(key) });

        var html = await response.Content.ReadAsStringAsync(Ct);
        html.Should().Contain("Wiederherstellungscodes");
        RecoveryCodeRegex().Matches(html).Should().HaveCount(10);
        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await users.GetTwoFactorEnabledAsync((await users.FindByEmailAsync(AdminEmail))!)).Should().BeTrue();
    }

    [Fact]
    public async Task Login_WithTwoFactorEnabled_RequiresAuthenticatorCode()
    {
        var (key, _) = await EnableTwoFactorAsync();
        var client = CreateClient();

        var login = await WebLogin.PostAsync(client, AdminEmail, AdminPassword, Ct);
        login.Headers.Location!.OriginalString.Should().Contain("Account/LoginWith2fa");
        (await client.GetAsync("/", Ct)).StatusCode.Should().Be(HttpStatusCode.Redirect, "the password alone does not sign in");

        var wrong = await PostFormAsync(client, login.Headers.Location.OriginalString, "login-with-2fa", new() { ["Input.TwoFactorCode"] = "000000" });
        WebUtility.HtmlDecode(await wrong.Content.ReadAsStringAsync(Ct)).Should().Contain("Der Code ist ungültig.");

        var right = await PostFormAsync(client, login.Headers.Location.OriginalString, "login-with-2fa", new() { ["Input.TwoFactorCode"] = Totp(key) });
        right.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await client.GetAsync("/", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_WithRecoveryCode_SignsInOnceWithEachCode()
    {
        var (_, codes) = await EnableTwoFactorAsync();
        var client = CreateClient();
        await WebLogin.PostAsync(client, AdminEmail, AdminPassword, Ct);

        var response = await PostFormAsync(client, "/Account/LoginWithRecoveryCode", "login-with-recovery-code", new() { ["Input.RecoveryCode"] = codes[0] });

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await client.GetAsync("/", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await users.CountRecoveryCodesAsync((await users.FindByEmailAsync(AdminEmail))!)).Should().Be(codes.Count - 1);
    }

    [Fact]
    public async Task Login_TwoFactorAttempts_AreRateLimitedLikeThePassword()
    {
        await EnableTwoFactorAsync();
        var client = CreateClient();
        var login = await WebLogin.PostAsync(client, AdminEmail, AdminPassword, Ct);
        var path = login.Headers.Location!.OriginalString;

        HttpResponseMessage? last = null;
        for (var i = 0; i < LoginRateLimiting.PermitLimit; i++)
        {
            last = await PostFormAsync(client, path, "login-with-2fa", new() { ["Input.TwoFactorCode"] = "000000" });
        }

        last!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "the password login used the first permit");
    }

    /// <summary>Enables TOTP for the admin like the setup page does; returns the key and the recovery codes.</summary>
    private async Task<(string Key, IReadOnlyList<string> RecoveryCodes)> EnableTwoFactorAsync()
    {
        CreateClient(); // starts the host, which seeds the admin
        await using var scope = _factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = (await users.FindByEmailAsync(AdminEmail))!;
        await users.ResetAuthenticatorKeyAsync(admin);
        await users.SetTwoFactorEnabledAsync(admin, true);
        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(admin, 10);
        return ((await users.GetAuthenticatorKeyAsync(admin))!, [.. codes!]);
    }

    private async Task<HttpClient> SignedInClientAsync()
    {
        var client = CreateClient();
        (await WebLogin.PostAsync(client, AdminEmail, AdminPassword, Ct)).StatusCode.Should().Be(HttpStatusCode.Redirect);
        return client;
    }

    /// <summary>Posts a statically rendered form with the antiforgery token of the page.</summary>
    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path, string formName, Dictionary<string, string> fields)
    {
        var page = await client.GetStringAsync(path, Ct);
        fields["_handler"] = formName;
        fields["__RequestVerificationToken"] = AntiforgeryTokenRegex().Match(page).Groups[1].Value;
        return await client.PostAsync(path, new FormUrlEncodedContent(fields), Ct);
    }

    /// <summary>Current TOTP code (RFC 6238: HMAC-SHA1, 30 s, 6 digits) for a Base32 key, as authenticator apps compute it.</summary>
    private static string Totp(string base32Key)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = string.Concat(base32Key.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant()
            .Select(c => Convert.ToString(alphabet.IndexOf(c, StringComparison.Ordinal), 2).PadLeft(5, '0')));
        var key = Enumerable.Range(0, bits.Length / 8).Select(i => Convert.ToByte(bits.Substring(i * 8, 8), 2)).ToArray();

        var counter = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counter);
        }

#pragma warning disable CA5350 // TOTP is defined with HMAC-SHA1 (RFC 6238), as used by Identity and the apps
        var hash = HMACSHA1.HashData(key, counter);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var code = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (code % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private HttpClient CreateClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""")]
    private static partial Regex AntiforgeryTokenRegex();

    [GeneratedRegex(@"data-uri=""([^""]+)""")]
    private static partial Regex DataUriRegex();

    [GeneratedRegex(@"data-testid=""authenticator-key"">([^<]+)<")]
    private static partial Regex KeyRegex();

    [GeneratedRegex(@"<code class=""recovery-code"">[A-Z0-9]{5}-[A-Z0-9]{5}</code>")]
    private static partial Regex RecoveryCodeRegex();
}
