using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.Mail;

/// <summary>
/// The connected mailbox of one provider (ADR-013): who it is, the encrypted refresh token and how the last syncs
/// went. There is at most one connection per provider.
/// </summary>
public sealed class MailConnection : Entity
{
    public const int AccountMaxLength = 320;
    public const int ErrorMaxLength = 500;

    // Required by EF Core.
    private MailConnection()
    {
        Provider = null!;
        Account = null!;
        ProtectedRefreshToken = null!;
    }

    public MailConnection(string provider, string account, string protectedRefreshToken, DateTimeOffset connectedAt)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("A provider is required.", nameof(provider));
        }

        Provider = provider.Trim();
        Account = RequiredAccount(account);
        ProtectedRefreshToken = RequiredToken(protectedRefreshToken);
        ConnectedAt = connectedAt.ToUniversalTime();
    }

    public string Provider { get; private set; }

    /// <summary>The mailbox address shown to the user.</summary>
    public string Account { get; private set; }

    /// <summary>The refresh token, encrypted with Data Protection (never stored in plain text).</summary>
    public string ProtectedRefreshToken { get; private set; }

    public DateTimeOffset ConnectedAt { get; private set; }

    public DateTimeOffset? LastSyncedAt { get; private set; }

    /// <summary>User-facing reason why the last sync failed; <c>null</c> after a successful one.</summary>
    public string? LastError { get; private set; }

    /// <summary>Replaces the token after a refresh (providers rotate refresh tokens).</summary>
    public void ReplaceRefreshToken(string protectedRefreshToken) => ProtectedRefreshToken = RequiredToken(protectedRefreshToken);

    /// <summary>Connects another or the same mailbox again with a fresh token.</summary>
    public void Reconnect(string account, string protectedRefreshToken, DateTimeOffset connectedAt)
    {
        Account = RequiredAccount(account);
        ProtectedRefreshToken = RequiredToken(protectedRefreshToken);
        ConnectedAt = connectedAt.ToUniversalTime();
        LastSyncedAt = null;
        LastError = null;
    }

    public void RecordSuccess(DateTimeOffset syncedAt)
    {
        LastSyncedAt = syncedAt.ToUniversalTime();
        LastError = null;
    }

    public void RecordFailure(string error)
    {
        var trimmed = string.IsNullOrWhiteSpace(error) ? "Unbekannter Fehler." : error.Trim();
        LastError = trimmed.Length <= ErrorMaxLength ? trimmed : string.Concat(trimmed.AsSpan(0, ErrorMaxLength - 1), "…");
    }

    private static string RequiredAccount(string account) =>
        string.IsNullOrWhiteSpace(account) || account.Trim().Length > AccountMaxLength
            ? throw new ArgumentException($"An account of 1 to {AccountMaxLength} characters is required.", nameof(account))
            : account.Trim();

    private static string RequiredToken(string token) =>
        string.IsNullOrWhiteSpace(token)
            ? throw new ArgumentException("A token is required.", nameof(token))
            : token;
}
