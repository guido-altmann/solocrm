using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.CalendarFeeds;

namespace SoloCrm.Web.Endpoints;

/// <summary>
/// Subscribable ICS feed of the tasks (SOL-6): <c>GET /calendar/{token}.ics</c>. Calendar apps cannot sign in, so the
/// secret token in the URL is the only authentication; it is stored hashed and can be replaced in the settings.
/// Unknown, replaced and malformed tokens all get the same 404.
/// </summary>
public static class CalendarFeedEndpoints
{
    public const string PathPrefix = "/calendar/";
    public const string FileExtension = ".ics";
    public const string RateLimitPolicyName = "calendar-feed";
    public const string ContentType = "text/calendar; charset=utf-8";

    public static string FeedPath(string token) => $"{PathPrefix}{token}{FileExtension}";

    public static IEndpointRouteBuilder MapCalendarFeed(this IEndpointRouteBuilder app)
    {
        app.MapGet(PathPrefix + "{token}" + FileExtension, GetFeed)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicyName)
            .ExcludeFromDescription();
        return app;
    }

    private static async Task<IResult> GetFeed(
        HttpContext context,
        string token,
        IQueryHandler<GetTaskFeed.Query, GetTaskFeed.Result> handler,
        IConfiguration configuration)
    {
        var baseUrl = configuration["App:BaseUrl"];
        var origin = string.IsNullOrWhiteSpace(baseUrl)
            ? $"{context.Request.Scheme}://{context.Request.Host}"
            : baseUrl.Trim().TrimEnd('/');

        var result = await handler.Handle(new GetTaskFeed.Query(token, origin), context.RequestAborted);
        if (result.IsFailure)
        {
            return Results.NotFound();
        }

        // The feed contains task titles: no shared caches.
        context.Response.Headers.CacheControl = "no-store";
        return Results.Text(result.Value.Content, ContentType);
    }

    /// <summary>Hides the token in a request path for logs (the token is a credential).</summary>
    public static string MaskPath(string path) =>
        path.StartsWith(PathPrefix, StringComparison.OrdinalIgnoreCase) && path.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase)
            ? PathPrefix + "{token}" + FileExtension
            : path;
}

/// <summary>30 requests per minute and client IP; calendar apps poll at most hourly, the limit slows down token guessing.</summary>
public sealed class CalendarFeedRateLimiting : IRateLimiterPolicy<string>
{
    public const int PermitLimit = 30;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected { get; } = (context, _) =>
    {
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var value) ? value : Window;
        context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        return ValueTask.CompletedTask;
    };

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return RateLimitPartition.GetFixedWindowLimiter($"ip:{httpContext.Connection.RemoteIpAddress}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = PermitLimit,
            Window = Window,
            QueueLimit = 0,
        });
    }
}
