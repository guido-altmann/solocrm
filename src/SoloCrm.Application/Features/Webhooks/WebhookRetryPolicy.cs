namespace SoloCrm.Application.Features.Webhooks;

/// <summary>
/// Exponential backoff for webhook deliveries (US-18 AK3): at most 6 attempts, the first one right away.
/// The delays add up to about 14.5 hours, so a receiver that is down overnight still gets the event.
/// </summary>
public static class WebhookRetryPolicy
{
    public const int MaxAttempts = 6;

    /// <summary>Delay after failed attempt 1 … 5.</summary>
    public static IReadOnlyList<TimeSpan> Delays { get; } =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(12),
    ];

    /// <summary>
    /// When to try again after <paramref name="failedAttempts"/> failed attempts, or <c>null</c> when the
    /// delivery is given up.
    /// </summary>
    public static DateTimeOffset? NextAttemptAt(int failedAttempts, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(failedAttempts, 1);
        return failedAttempts >= MaxAttempts ? null : now + Delays[failedAttempts - 1];
    }
}
