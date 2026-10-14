namespace SoloCrm.Application.Abstractions;

/// <summary>
/// Connects the mailbox of a provider to SoloCRM (ADR-013): sign-in with the authorization code flow, status and
/// disconnecting. The tokens never leave the infrastructure; they are stored encrypted.
/// </summary>
public interface IMailConnector
{
    /// <summary>Whether the provider is set up in the configuration (client id, tenant and secret).</summary>
    bool IsConfigured { get; }

    /// <summary>The address the user signs in at. <paramref name="codeChallenge"/> is the S256 PKCE challenge.</summary>
    /// <exception cref="InvalidOperationException">The provider is not configured.</exception>
    Uri BuildAuthorizationUri(string redirectUri, string state, string codeChallenge);

    /// <summary>Completes the sign-in and stores the connection. Returns the connected account (mailbox address).</summary>
    /// <exception cref="MailSourceException">Microsoft rejected the code or the account cannot be read.</exception>
    Task<string> ConnectAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken);

    /// <summary>Removes the stored token and the sync progress. Imported activities stay.</summary>
    Task DisconnectAsync(CancellationToken cancellationToken);

    /// <summary>The current connection, or <c>null</c> if no mailbox is connected.</summary>
    Task<MailConnectionInfo?> GetConnectionAsync(CancellationToken cancellationToken);

    /// <summary>Notes the outcome of a sync for the status display; <paramref name="error"/> is <c>null</c> on success.</summary>
    Task RecordSyncAsync(string? error, CancellationToken cancellationToken);
}

public sealed record MailConnectionInfo(string Provider, string Account, DateTimeOffset ConnectedAt, DateTimeOffset? LastSyncedAt, string? LastError);
