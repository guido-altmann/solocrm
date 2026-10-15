using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.CalendarFeeds;

/// <summary>
/// The secret behind the subscribable ICS feed of the tasks (SOL-6). Only the SHA-256 hash of the token is stored;
/// the feed URL with the plaintext token is shown once. At most one feed exists: generating a new one replaces it.
/// </summary>
public sealed class CalendarFeed : Entity
{
    /// <summary><see cref="LastUsedAt"/> is written at most this often, since calendar apps poll regularly.</summary>
    public static readonly TimeSpan LastUsedPrecision = TimeSpan.FromMinutes(1);

    // Required by EF Core.
    private CalendarFeed()
    {
        TokenHash = null!;
    }

    private CalendarFeed(string tokenHash)
    {
        TokenHash = tokenHash;
    }

    /// <summary>SHA-256 of the token as lowercase hex.</summary>
    public string TokenHash { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    public static CalendarFeed Create(string tokenHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        return new CalendarFeed(tokenHash);
    }

    /// <summary>Records a retrieval; returns <c>false</c> when the stored value is recent enough and nothing changed.</summary>
    public bool RecordUse(DateTimeOffset now)
    {
        if (LastUsedAt is { } last && now - last < LastUsedPrecision)
        {
            return false;
        }

        LastUsedAt = now;
        return true;
    }
}
