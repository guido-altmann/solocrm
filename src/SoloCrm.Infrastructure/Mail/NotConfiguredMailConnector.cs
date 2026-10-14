using SoloCrm.Application.Abstractions;

namespace SoloCrm.Infrastructure.Mail;

/// <summary>Used while <c>Mail:Graph</c> is not configured: nothing can be connected, there is no connection.</summary>
internal sealed class NotConfiguredMailConnector : IMailConnector
{
    public bool IsConfigured => false;

    public Uri BuildAuthorizationUri(string redirectUri, string state, string codeChallenge) =>
        throw new InvalidOperationException("The Microsoft 365 mailbox is not configured (Mail:Graph).");

    public Task<string> ConnectAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("The Microsoft 365 mailbox is not configured (Mail:Graph).");

    public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<MailConnectionInfo?> GetConnectionAsync(CancellationToken cancellationToken) => Task.FromResult<MailConnectionInfo?>(null);

    public Task RecordSyncAsync(string? error, CancellationToken cancellationToken) => Task.CompletedTask;
}
