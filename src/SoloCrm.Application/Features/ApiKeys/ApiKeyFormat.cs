using System.Security.Cryptography;
using System.Text;
using SoloCrm.Domain.ApiKeys;

namespace SoloCrm.Application.Features.ApiKeys;

/// <summary>
/// Key format <c>scrm_&lt;prefix&gt;_&lt;secret&gt;</c>: 8 random characters as prefix (stored in plaintext, used for the
/// lookup) and 32 random bytes as hex. The key has enough entropy for a plain SHA-256 hash; a slow password hash
/// is not needed.
/// </summary>
public static class ApiKeyFormat
{
    public const string Scheme = "scrm";

    private const string PrefixAlphabet = "abcdefghijkmnpqrstuvwxyz23456789";
    private const int SecretHexLength = 64;

    public static GeneratedApiKey Generate()
    {
        var prefix = RandomNumberGenerator.GetString(PrefixAlphabet, ApiKey.PrefixLength);
        var secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretHexLength / 2));
        var key = $"{Scheme}_{prefix}_{secret}";
        return new GeneratedApiKey(key, prefix, Hash(key));
    }

    /// <summary>Checks the shape of a key without touching the database and extracts its prefix.</summary>
    public static bool TryGetPrefix(string? key, out string prefix)
    {
        prefix = "";
        var parts = key?.Split('_');
        if (parts is not [Scheme, { Length: ApiKey.PrefixLength } candidate, { Length: SecretHexLength } secret]
            || !candidate.All(PrefixAlphabet.Contains)
            || !secret.All(char.IsAsciiHexDigitLower))
        {
            return false;
        }

        prefix = candidate;
        return true;
    }

    public static string Hash(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    /// <summary>Compares two hex hashes in constant time.</summary>
    public static bool HashesEqual(string expected, string actual) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(actual));

    /// <summary>Shown in lists instead of the key, e.g. <c>scrm_ab12cd34_…</c>.</summary>
    public static string Display(string prefix) => $"{Scheme}_{prefix}_…";
}

public sealed record GeneratedApiKey(string Key, string Prefix, string Hash);
