using System.Security.Cryptography;
using System.Text;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.CalendarFeeds;

/// <summary>
/// The secret in the feed URL: 32 random bytes as base64url (43 characters). Like API keys, it has enough entropy
/// for a plain SHA-256 hash; the feed is found by that hash, so no secret is compared character by character.
/// </summary>
public static class CalendarFeedToken
{
    public const int Length = 43;

    public static GeneratedToken Generate()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return new GeneratedToken(token, Hash(token));
    }

    /// <summary>Checks the shape of a token without touching the database.</summary>
    public static bool IsWellFormed(string? token) =>
        token is { Length: Length } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public static string Hash(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}

public sealed record GeneratedToken(string Token, string Hash);

public static class CalendarFeedErrors
{
    public static Error NotFound { get; } =
        Error.NotFound("CalendarFeed.NotFound", "Es ist kein Kalender-Feed eingerichtet.");

    /// <summary>Deliberately the same for unknown, replaced and malformed tokens.</summary>
    public static Error Invalid { get; } =
        Error.Failure("CalendarFeed.Invalid", "Der Feed-Link ist ungültig oder wurde ersetzt.");
}
