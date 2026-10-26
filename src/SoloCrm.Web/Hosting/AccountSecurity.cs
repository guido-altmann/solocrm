using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace SoloCrm.Web.Hosting;

/// <summary>
/// Sign-in hardening (SOL-30, ADR-004): account lockout, password rules and explicit cookie settings.
/// The lockout is short on purpose: the only account that can be locked is the single admin.
/// </summary>
public static class AccountSecurity
{
    public const int MaxFailedAccessAttempts = 10;

    public const int PasswordMinLength = 12;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan CookieLifetime = TimeSpan.FromDays(14);

    private static readonly string[] IdentityCookieSchemes =
    [
        IdentityConstants.ApplicationScheme,
        IdentityConstants.ExternalScheme,
        IdentityConstants.TwoFactorUserIdScheme,
        IdentityConstants.TwoFactorRememberMeScheme,
    ];

    /// <summary>Failed password, authenticator and recovery code sign-ins all count towards the lockout.</summary>
    public static void Configure(IdentityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = MaxFailedAccessAttempts;
        options.Lockout.DefaultLockoutTimeSpan = LockoutDuration;

        // Length instead of composition rules (NIST SP 800-63B).
        options.Password.RequiredLength = PasswordMinLength;
        options.Password.RequiredUniqueChars = 4;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
    }

    /// <summary>
    /// HttpOnly and SameSite=Lax for all Identity cookies, Secure always outside Development (local development and the
    /// integration tests run on plain http). The sign-in cookie lasts 14 days and slides with use.
    /// </summary>
    public static IServiceCollection AddIdentityCookieHardening(this IServiceCollection services, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var securePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

        foreach (var scheme in IdentityCookieSchemes)
        {
            services.Configure<CookieAuthenticationOptions>(scheme, options =>
            {
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = securePolicy;
            });
        }

        services.Configure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options =>
        {
            options.ExpireTimeSpan = CookieLifetime;
            options.SlidingExpiration = true;
        });

        return services;
    }
}
