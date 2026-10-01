using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.ApiKeys;

/// <summary>
/// Grants access to the REST API (SPEC 2.4, US-17). Only the SHA-256 hash of the key is stored; the plaintext is
/// shown once at creation. Revoked keys stay for traceability.
/// </summary>
public sealed class ApiKey : Entity
{
    public const int NameMaxLength = 100;
    public const int PrefixLength = 8;

    /// <summary><see cref="LastUsedAt"/> is written at most this often to keep API calls free of extra writes.</summary>
    public static readonly TimeSpan LastUsedPrecision = TimeSpan.FromMinutes(1);

    // Required by EF Core.
    private ApiKey()
    {
        Name = null!;
        Prefix = null!;
        KeyHash = null!;
    }

    private ApiKey(string name, string prefix, string keyHash)
    {
        Name = name;
        Prefix = prefix;
        KeyHash = keyHash;
    }

    public string Name { get; private set; }

    /// <summary>Random, non-secret part of the key; identifies it in lists and lookups.</summary>
    public string Prefix { get; private set; }

    /// <summary>SHA-256 of the full key as lowercase hex.</summary>
    public string KeyHash { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    public static ApiKey Create(string name, string prefix, string keyHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmed = name.Trim();
        if (trimmed.Length > NameMaxLength)
        {
            throw new ArgumentException($"An API key name has at most {NameMaxLength} characters.", nameof(name));
        }

        if (prefix is not { Length: PrefixLength })
        {
            throw new ArgumentException($"The prefix has exactly {PrefixLength} characters.", nameof(prefix));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(keyHash);
        return new ApiKey(trimmed, prefix, keyHash);
    }

    /// <summary>Revoking twice keeps the first point in time.</summary>
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;

    /// <summary>Records a use; returns <c>false</c> when the stored value is recent enough and nothing changed.</summary>
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
