using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.Nextcloud;

/// <summary>
/// The connected Nextcloud account (ADR-014): server, user, the encrypted app password, the collections SoloCRM
/// created there and how the last syncs went. There is at most one connection.
/// </summary>
public sealed class NextcloudConnection : Entity
{
    public const int ServerUrlMaxLength = 500;
    public const int UserNameMaxLength = 200;
    public const int HrefMaxLength = 1000;
    public const int SyncTokenMaxLength = 1000;
    public const int ErrorMaxLength = 500;

    // Required by EF Core.
    private NextcloudConnection()
    {
        ServerUrl = null!;
        UserName = null!;
        ProtectedAppPassword = null!;
    }

    public NextcloudConnection(string serverUrl, string userName, string protectedAppPassword, DateTimeOffset connectedAt)
    {
        ServerUrl = RequiredServerUrl(serverUrl);
        UserName = RequiredUserName(userName);
        ProtectedAppPassword = RequiredPassword(protectedAppPassword);
        ConnectedAt = connectedAt.ToUniversalTime();
    }

    /// <summary>Base address of the Nextcloud instance as entered, without trailing slash.</summary>
    public string ServerUrl { get; private set; }

    public string UserName { get; private set; }

    /// <summary>The Nextcloud app password, encrypted with Data Protection (never stored in plain text).</summary>
    public string ProtectedAppPassword { get; private set; }

    /// <summary>Path of the address book „SoloCRM“ on the server; <c>null</c> until it is found or created.</summary>
    public string? AddressBookHref { get; private set; }

    /// <summary>Path of the task list „SoloCRM“ (only <c>VTODO</c>); <c>null</c> until it is found or created.</summary>
    public string? TaskListHref { get; private set; }

    /// <summary>
    /// <c>sync-token</c> (RFC 6578) of the task list after the last incoming phase; <c>null</c> before the first one or
    /// after the server rejected it, which makes the next run compare all ETags.
    /// </summary>
    public string? TaskSyncToken { get; private set; }

    public DateTimeOffset ConnectedAt { get; private set; }

    public DateTimeOffset? LastSyncedAt { get; private set; }

    /// <summary>User-facing reason why the last sync failed; <c>null</c> after a successful one.</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// The server rejected the credentials (<c>401</c>). The sync pauses until new credentials are saved, so that
    /// a revoked app password does not fail every few minutes.
    /// </summary>
    public bool IsCredentialRejected { get; private set; }

    /// <summary>Whether <paramref name="serverUrl"/> and <paramref name="userName"/> denote the connected account.</summary>
    public bool IsSameAccount(string serverUrl, string userName) =>
        string.Equals(ServerUrl, NormalizeServerUrl(serverUrl), StringComparison.OrdinalIgnoreCase)
        && string.Equals(UserName, userName.Trim(), StringComparison.Ordinal);

    /// <summary>Saves new credentials (same or other account) and resumes the sync.</summary>
    public void Reconnect(string serverUrl, string userName, string protectedAppPassword, DateTimeOffset connectedAt)
    {
        if (!IsSameAccount(serverUrl, userName))
        {
            // The collections belong to the other account.
            AddressBookHref = null;
            TaskListHref = null;
            TaskSyncToken = null;
        }

        ServerUrl = RequiredServerUrl(serverUrl);
        UserName = RequiredUserName(userName);
        ProtectedAppPassword = RequiredPassword(protectedAppPassword);
        ConnectedAt = connectedAt.ToUniversalTime();
        LastSyncedAt = null;
        LastError = null;
        IsCredentialRejected = false;
    }

    public void UseAddressBook(string href) => AddressBookHref = RequiredHref(href);

    /// <summary>Sets the task list; a different list starts the incoming phase from scratch.</summary>
    public void UseTaskList(string href)
    {
        var required = RequiredHref(href);
        if (required != TaskListHref)
        {
            TaskSyncToken = null;
        }

        TaskListHref = required;
    }

    /// <summary>Notes where the next incoming phase continues; <c>null</c> forces a full comparison.</summary>
    public void AdvanceTaskSyncToken(string? token) =>
        TaskSyncToken = string.IsNullOrWhiteSpace(token) || token.Length > SyncTokenMaxLength ? null : token;

    public void RecordSuccess(DateTimeOffset syncedAt)
    {
        LastSyncedAt = syncedAt.ToUniversalTime();
        LastError = null;
    }

    public void RecordFailure(string error) => LastError = Truncate(error);

    /// <summary>Notes a <c>401</c> and pauses the sync until <see cref="Reconnect"/>.</summary>
    public void RejectCredentials(string error)
    {
        LastError = Truncate(error);
        IsCredentialRejected = true;
    }

    /// <summary>Trims the value and removes a trailing slash, so that the same server is always stored the same way.</summary>
    public static string NormalizeServerUrl(string serverUrl) => serverUrl.Trim().TrimEnd('/');

    private static string RequiredServerUrl(string serverUrl)
    {
        var normalized = string.IsNullOrWhiteSpace(serverUrl) ? "" : NormalizeServerUrl(serverUrl);
        return normalized.Length is > 0 and <= ServerUrlMaxLength
            ? normalized
            : throw new ArgumentException($"A server URL of 1 to {ServerUrlMaxLength} characters is required.", nameof(serverUrl));
    }

    private static string RequiredUserName(string userName) =>
        string.IsNullOrWhiteSpace(userName) || userName.Trim().Length > UserNameMaxLength
            ? throw new ArgumentException($"A user name of 1 to {UserNameMaxLength} characters is required.", nameof(userName))
            : userName.Trim();

    private static string RequiredPassword(string password) =>
        string.IsNullOrWhiteSpace(password)
            ? throw new ArgumentException("A password is required.", nameof(password))
            : password;

    private static string RequiredHref(string href) =>
        string.IsNullOrWhiteSpace(href) || href.Length > HrefMaxLength
            ? throw new ArgumentException($"A href of 1 to {HrefMaxLength} characters is required.", nameof(href))
            : href;

    private static string Truncate(string error)
    {
        var trimmed = string.IsNullOrWhiteSpace(error) ? "Unbekannter Fehler." : error.Trim();
        return trimmed.Length <= ErrorMaxLength ? trimmed : string.Concat(trimmed.AsSpan(0, ErrorMaxLength - 1), "…");
    }
}
