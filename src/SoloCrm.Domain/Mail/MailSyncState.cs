using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.Mail;

/// <summary>
/// Where the next sync of one folder continues (ADR-013): the provider's delta token, or <c>null</c> before the
/// first sync (which loads the configured backfill period).
/// </summary>
public sealed class MailSyncState : Entity
{
    public const int DeltaTokenMaxLength = 4000;

    // Required by EF Core.
    private MailSyncState()
    {
        Provider = null!;
    }

    public MailSyncState(string provider, MailFolder folder)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("A provider is required.", nameof(provider));
        }

        Provider = provider.Trim();
        Folder = Enum.IsDefined(folder) ? folder : throw new ArgumentOutOfRangeException(nameof(folder), folder, "Unknown folder.");
    }

    public string Provider { get; private set; }

    public MailFolder Folder { get; private set; }

    public string? DeltaToken { get; private set; }

    public DateTimeOffset? LastSyncedAt { get; private set; }

    /// <summary>Stores the continuation point after a successful sync.</summary>
    public void Advance(string deltaToken, DateTimeOffset syncedAt)
    {
        if (string.IsNullOrWhiteSpace(deltaToken) || deltaToken.Length > DeltaTokenMaxLength)
        {
            throw new ArgumentException($"A delta token of 1 to {DeltaTokenMaxLength} characters is required.", nameof(deltaToken));
        }

        DeltaToken = deltaToken;
        LastSyncedAt = syncedAt.ToUniversalTime();
    }

    /// <summary>Forgets the token, e.g. after the provider reported it as expired; the next sync starts from scratch.</summary>
    public void Reset() => DeltaToken = null;
}
