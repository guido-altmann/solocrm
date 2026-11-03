using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using SoloCrm.Application.Features.ApiKeys;

namespace SoloCrm.Web.Endpoints;

/// <summary>
/// 60 requests per minute and API key (US-17 AK2). Partitioned by the key prefix, so the limit applies before the
/// key is checked against the database; requests without a well-formed key share a budget per client IP.
/// On top, <see cref="CreateClientIpLimiter"/> limits all API requests of one client IP (SOL-34, audit F-04), so that
/// random prefixes do not get around the limit. Rejections are answered with 429, <c>Retry-After</c> and Problem Details.
/// </summary>
public sealed class ApiRateLimiting : IRateLimiterPolicy<string>
{
    public const string PolicyName = "api";

    public const int PermitLimit = 60;

    /// <summary>Generous: several keys (e.g. n8n workflows) usually share the IP of one server.</summary>
    public const int ClientIpPermitLimit = 300;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected { get; } =
        (context, _) => RejectAsync(context, $"Höchstens {PermitLimit} Anfragen pro Minute und API-Key.");

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var header = httpContext.Request.Headers[ApiKeyAuthentication.HeaderName].ToString();
        var partition = ApiKeyFormat.TryGetPrefix(header, out var prefix)
            ? $"key:{prefix}"
            : $"ip:{httpContext.Connection.RemoteIpAddress}";

        return FixedWindow(partition, PermitLimit);
    }

    /// <summary>
    /// The global limiter of the app (it runs before the endpoint policies): <see cref="ClientIpPermitLimit"/> API
    /// requests per minute and client IP; all other requests are not limited here.
    /// </summary>
    public static PartitionedRateLimiter<HttpContext> CreateClientIpLimiter() =>
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
            context.Request.Path.StartsWithSegments(ApiEndpoints.BasePath)
                ? FixedWindow($"ip:{context.Connection.RemoteIpAddress}", ClientIpPermitLimit)
                : RateLimitPartition.GetNoLimiter(string.Empty));

    /// <summary>Rejections of the global limiter; endpoint policies have their own handlers.</summary>
    public static ValueTask OnClientIpRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken) =>
        RejectAsync(context, $"Höchstens {ClientIpPermitLimit} API-Anfragen pro Minute von dieser Adresse.");

    private static RateLimitPartition<string> FixedWindow(string partition, int permitLimit) =>
        RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = Window,
            QueueLimit = 0,
        });

    private static async ValueTask RejectAsync(OnRejectedContext context, string limit)
    {
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var value) ? value : Window;
        var seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
        context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);

        await TypedResults.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Zu viele Anfragen",
                detail: $"{limit} Bitte in {seconds} s erneut versuchen.")
            .ExecuteAsync(context.HttpContext);
    }
}
