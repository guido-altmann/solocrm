using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using SoloCrm.Infrastructure.Identity;

namespace SoloCrm.Web.Components.Account;

/// <summary>
/// Guards the pages that change sign-in factors (passkeys, authenticator app, 2FA, recovery codes; SOL-34, audit F-03):
/// they require the password to be confirmed within <see cref="Window"/>, so that a taken-over session alone cannot
/// add or remove a factor. The confirmation is a separate cookie, encrypted with Data Protection and bound to the user.
/// After every change the security stamp is renewed (other sessions are signed out) and a security event is logged.
/// </summary>
internal sealed partial class FactorChanges(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IdentityRedirectManager redirectManager,
    IDataProtectionProvider dataProtection,
    TimeProvider timeProvider,
    IHostEnvironment environment,
    ILogger<FactorChanges> logger)
{
    public const string CookieName = ".SoloCrm.FactorChangeConfirmed";
    public const string ConfirmPath = "Account/Manage/ConfirmPassword";
    public const string ManagePathPrefix = "Account/Manage/";

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private const string CookiePath = "/Account";

    private readonly IDataProtector _protector = dataProtection.CreateProtector("SoloCrm.FactorChanges.v1");

    /// <summary>
    /// Whether the password was confirmed recently; otherwise redirects to the confirmation page, which returns to
    /// the current page. Call it on every request of a guarded page (GET and POST).
    /// </summary>
    public bool EnsureConfirmed(HttpContext context, ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(user);

        if (IsConfirmed(context, user))
        {
            return true;
        }

        redirectManager.RedirectTo(ConfirmPath, new() { ["ReturnUrl"] = context.Request.Path.Value?.TrimStart('/') });
        return false;
    }

    /// <summary>Notes the confirmed password for <see cref="Window"/>.</summary>
    public void Confirm(HttpContext context, ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(user);

        var expiresAt = timeProvider.GetUtcNow() + Window;
        var payload = string.Create(CultureInfo.InvariantCulture, $"{user.Id}|{expiresAt.ToUnixTimeSeconds()}");
        context.Response.Cookies.Append(CookieName, _protector.Protect(payload), new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Strict,
            Secure = context.Request.IsHttps || !environment.IsDevelopment(),
            Path = CookiePath,
            Expires = expiresAt,
        });
    }

    /// <summary>Forgets the confirmation, e.g. at sign-out.</summary>
    public static void Clear(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.Cookies.Delete(CookieName, new CookieOptions { Path = CookiePath });
    }

    /// <summary>
    /// Completes a factor change: renews the security stamp (signs out other sessions at their next validation), keeps
    /// the current session signed in and logs the security event.
    /// </summary>
    public async Task CompleteAsync(ApplicationUser user, string change)
    {
        ArgumentNullException.ThrowIfNull(user);

        await userManager.UpdateSecurityStampAsync(user);
        await signInManager.RefreshSignInAsync(user);
        LogFactorChanged(logger, change, user.Id);
    }

    /// <summary>Only pages below <c>Account/Manage/</c> are valid targets after the confirmation (no open redirect).</summary>
    public static string SafeReturnUrl(string? returnUrl) =>
        returnUrl is not null
        && returnUrl.StartsWith(ManagePathPrefix, StringComparison.Ordinal)
        && !returnUrl.Contains("//", StringComparison.Ordinal)
        && !returnUrl.Contains('\\', StringComparison.Ordinal)
        && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
            ? returnUrl
            : "Account/Manage";

    private bool IsConfirmed(HttpContext context, ApplicationUser user)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var value) || string.IsNullOrEmpty(value))
        {
            return false;
        }

        string payload;
        try
        {
            payload = _protector.Unprotect(value);
        }
        catch (CryptographicException)
        {
            return false;
        }

        var parts = payload.Split('|');
        return parts.Length == 2
            && string.Equals(parts[0], user.Id, StringComparison.Ordinal)
            && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAt)
            && timeProvider.GetUtcNow().ToUnixTimeSeconds() < expiresAt;
    }

    // Security event (SOL-34); like all logs only with the user id (docs/datenschutz.md).
    [LoggerMessage(Level = LogLevel.Warning, Message = "Sign-in factor changed: {Change} (user {UserId})")]
    private static partial void LogFactorChanged(ILogger logger, string change, string userId);
}
