using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SoloCrm.Application.Features.Webhooks;

/// <summary>
/// Signs webhook requests (SPEC 5, iteration 5 decision 6): HMAC-SHA256 with the subscription secret over
/// <c>&lt;timestamp&gt;.&lt;body&gt;</c>, sent as <c>X-SoloCrm-Signature: sha256=&lt;hex&gt;</c> together with
/// <c>X-SoloCrm-Timestamp</c> (Unix seconds).
/// </summary>
public static class WebhookSignature
{
    public const string SignatureHeader = "X-SoloCrm-Signature";
    public const string TimestampHeader = "X-SoloCrm-Timestamp";
    public const string EventHeader = "X-SoloCrm-Event";

    private const string Scheme = "sha256=";

    public static string Compute(string secret, long timestamp, string body)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        ArgumentNullException.ThrowIfNull(body);

        var signedContent = Encoding.UTF8.GetBytes(timestamp.ToString(CultureInfo.InvariantCulture) + "." + body);
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signedContent);
        return Scheme + Convert.ToHexStringLower(hash);
    }

    /// <summary>The receiver-side check as documented for n8n: signature in constant time and age below the tolerance.</summary>
    public static bool Verify(string secret, string timestampHeader, string body, string signatureHeader, DateTimeOffset now, TimeSpan tolerance)
    {
        ArgumentNullException.ThrowIfNull(signatureHeader);

        if (!long.TryParse(timestampHeader, NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp)
            || Math.Abs(now.ToUnixTimeSeconds() - timestamp) > tolerance.TotalSeconds)
        {
            return false;
        }

        var expected = Encoding.ASCII.GetBytes(Compute(secret, timestamp, body));
        var actual = Encoding.ASCII.GetBytes(signatureHeader);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    /// <summary>A new signing secret: 32 random bytes as lowercase hex.</summary>
    public static string GenerateSecret() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
}
