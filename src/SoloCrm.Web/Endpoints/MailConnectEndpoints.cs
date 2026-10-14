using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Mail;

namespace SoloCrm.Web.Endpoints;

/// <summary>
/// Sign-in to Microsoft 365 for the mail sync (ADR-013): <c>/mail/connect</c> redirects to Microsoft, <c>/mail/callback</c>
/// completes the authorization code flow with PKCE and returns to the settings with an outcome code. State and PKCE
/// verifier travel in a short-lived, encrypted, HttpOnly cookie. Both endpoints need the signed-in user.
/// </summary>
public static partial class MailConnectEndpoints
{
    public const string ConnectPath = "/mail/connect";
    public const string CallbackPath = "/mail/callback";
    public const string CookieName = ".SoloCrm.MailConnect";
    public const string ProtectorPurpose = "SoloCrm.MailConnect";

    /// <summary>How long the sign-in at Microsoft may take.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public static IEndpointRouteBuilder MapMailConnect(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/mail").RequireAuthorization().ExcludeFromDescription();
        group.MapGet("/connect", Connect);
        group.MapGet("/callback", Callback);
        return app;
    }

    private static IResult Connect(
        HttpContext context, IMailConnector connector, IDataProtectionProvider dataProtection, IConfiguration configuration, TimeProvider time)
    {
        if (!connector.IsConfigured)
        {
            return Results.Redirect(SettingsUrl("notconfigured"));
        }

        var state = RandomToken();
        var verifier = RandomToken();
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var expires = time.GetUtcNow().Add(Lifetime).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        context.Response.Cookies.Append(
            CookieName,
            dataProtection.CreateProtector(ProtectorPurpose).Protect($"{state}|{verifier}|{expires}"),
            CookieOptionsFor(context, Lifetime));

        return Results.Redirect(connector.BuildAuthorizationUri(RedirectUri(context, configuration), state, challenge).AbsoluteUri);
    }

    private static async Task<IResult> Callback(
        HttpContext context,
        string? code,
        string? state,
        string? error,
        ICommandHandler<ConnectMail.Command, ConnectMail.Result> connect,
        IDataProtectionProvider dataProtection,
        IConfiguration configuration,
        TimeProvider time,
        ILoggerFactory loggerFactory)
    {
        var cookie = context.Request.Cookies[CookieName];
        context.Response.Cookies.Delete(CookieName, CookieOptionsFor(context, TimeSpan.Zero));

        if (!TryReadCookie(cookie, dataProtection, time, out var expectedState, out var verifier))
        {
            return Results.Redirect(SettingsUrl("expired"));
        }

        if (state is null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(expectedState)))
        {
            return Results.Redirect(SettingsUrl("failed"));
        }

        if (error is not null)
        {
            // E.g. the user declined the permissions ("access_denied").
            return Results.Redirect(SettingsUrl("denied"));
        }

        var result = await connect.Handle(new ConnectMail.Command(code, verifier, RedirectUri(context, configuration)), context.RequestAborted);
        if (result.IsFailure)
        {
            LogConnectFailed(loggerFactory.CreateLogger("SoloCrm.Web.Endpoints.MailConnect"), result.Error.Code);
            return Results.Redirect(SettingsUrl("failed"));
        }

        return Results.Redirect(SettingsUrl("connected"));
    }

    private static bool TryReadCookie(
        string? cookie, IDataProtectionProvider dataProtection, TimeProvider time, out string state, out string verifier)
    {
        state = verifier = string.Empty;
        if (string.IsNullOrEmpty(cookie))
        {
            return false;
        }

        try
        {
            var parts = dataProtection.CreateProtector(ProtectorPurpose).Unprotect(cookie).Split('|');
            if (parts.Length != 3
                || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var expires)
                || time.GetUtcNow().ToUnixTimeSeconds() > expires)
            {
                return false;
            }

            (state, verifier) = (parts[0], parts[1]);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary><c>App:BaseUrl</c> if set (behind the reverse proxy), else the address of the request.</summary>
    private static string RedirectUri(HttpContext context, IConfiguration configuration)
    {
        var baseUrl = configuration["App:BaseUrl"];
        var origin = string.IsNullOrWhiteSpace(baseUrl)
            ? $"{context.Request.Scheme}://{context.Request.Host}"
            : baseUrl.Trim().TrimEnd('/');
        return origin + CallbackPath;
    }

    private static CookieOptions CookieOptionsFor(HttpContext context, TimeSpan maxAge) => new()
    {
        HttpOnly = true,
        Secure = context.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/mail",
        MaxAge = maxAge,
        IsEssential = true,
    };

    private static string SettingsUrl(string outcome) => $"/settings?mail={outcome}";

    private static string RandomToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connecting the mailbox failed: {Code}")]
    private static partial void LogConnectFailed(ILogger logger, string code);

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
