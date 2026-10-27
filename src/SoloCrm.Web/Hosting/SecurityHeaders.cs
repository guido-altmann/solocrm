using System.Security.Cryptography;

namespace SoloCrm.Web.Hosting;

/// <summary>
/// Security headers for every response (SOL-32, audit F-08). The CSP only allows same-origin resources; the one
/// inline script of the app, Blazor's import map, carries a per-request nonce. Styles need 'unsafe-inline' because
/// MudBlazor renders style attributes and a theme style element. Scalar renders an inline bootstrap script and
/// gets its own, slightly weaker policy.
/// </summary>
public static class SecurityHeaders
{
    public const string ScalarPath = "/scalar";

    private const string NonceItemKey = "csp-nonce";

    private const string PermissionsPolicy = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            // Hex instead of Base64: no characters that HTML encoding would turn into entities.
            var nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
            context.Items[NonceItemKey] = nonce;

            // OnStarting, so the headers survive the exception handler, which clears the response headers.
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers["Referrer-Policy"] = "no-referrer";
                headers["Permissions-Policy"] = PermissionsPolicy;
                headers.ContentSecurityPolicy = ContentSecurityPolicy(context.Request.Path, nonce);
                return Task.CompletedTask;
            });

            await next(context);
        });

    /// <summary>The nonce for inline scripts of the current request.</summary>
    public static string? GetCspNonce(this HttpContext context) =>
        context.Items.TryGetValue(NonceItemKey, out var nonce) ? nonce as string : null;

    public static string ContentSecurityPolicy(PathString path, string nonce)
    {
        var scripts = path.StartsWithSegments(ScalarPath) ? "'self' 'unsafe-inline'" : $"'self' 'nonce-{nonce}'";

        // 'self' in connect-src also covers the Blazor WebSocket on the same host (CSP Level 3).
        return "default-src 'self'; " +
            $"script-src {scripts}; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data:; " +
            "font-src 'self' data:; " +
            "connect-src 'self'; " +
            "object-src 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self'; " +
            "frame-ancestors 'none'";
    }
}
