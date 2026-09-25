using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace SoloCrm.Web.Hosting;

/// <summary>
/// Throttles sign-in attempts per client IP (SPEC 6: rate limiting on login).
/// Only POSTs count, so rendering the login forms is never limited. All login forms
/// (password, 2FA, recovery code) share one budget per IP.
/// </summary>
public static class LoginRateLimiting
{
    public const string PolicyName = "login";

    public const int PermitLimit = 5;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddLoginRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = OnRejectedAsync;

            options.AddPolicy(PolicyName, context =>
            {
                if (!HttpMethods.IsPost(context.Request.Method))
                {
                    return RateLimitPartition.GetNoLimiter(string.Empty);
                }

                // Real client IP: UseForwardedHeaders runs before the rate limiter.
                var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = PermitLimit,
                    Window = Window,
                    QueueLimit = 0,
                });
            });
        });

        return services;
    }

    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        response.ContentType = "text/plain; charset=utf-8";
        await response.WriteAsync("Zu viele Anmeldeversuche. Bitte in einer Minute erneut versuchen.", cancellationToken);
    }
}
