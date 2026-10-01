using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using SoloCrm.Application.Features.ApiKeys;

namespace SoloCrm.Web.Endpoints;

/// <summary>
/// 60 requests per minute and API key (US-17 AK2). Partitioned by the key prefix, so the limit applies before the
/// key is checked against the database; requests without a well-formed key share a budget per client IP.
/// Rejections are answered with 429, <c>Retry-After</c> and Problem Details.
/// </summary>
public sealed class ApiRateLimiting : IRateLimiterPolicy<string>
{
    public const string PolicyName = "api";

    public const int PermitLimit = 60;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected { get; } = OnRejectedAsync;

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var header = httpContext.Request.Headers[ApiKeyAuthentication.HeaderName].ToString();
        var partition = ApiKeyFormat.TryGetPrefix(header, out var prefix)
            ? $"key:{prefix}"
            : $"ip:{httpContext.Connection.RemoteIpAddress}";

        return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = PermitLimit,
            Window = Window,
            QueueLimit = 0,
        });
    }

    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var value) ? value : Window;
        var seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
        context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);

        await TypedResults.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Zu viele Anfragen",
                detail: $"Höchstens {PermitLimit} Anfragen pro Minute und API-Key. Bitte in {seconds} s erneut versuchen.")
            .ExecuteAsync(context.HttpContext);
    }
}
