using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Mail;

/// <summary>State of the mailbox connection for the settings page (ADR-013).</summary>
public static class GetMailStatus
{
    public sealed record Query;

    /// <param name="IsConfigured">The Entra app is set up in the configuration (<c>Mail__Graph__*</c>).</param>
    /// <param name="Connection">The connected mailbox, or <c>null</c>.</param>
    /// <param name="BackfillDays">Days loaded at the first sync.</param>
    public sealed record Result(bool IsConfigured, MailConnectionInfo? Connection, int BackfillDays);

    public sealed class Handler(IMailConnector connector, MailSyncOptions options) : IQueryHandler<Query, Result>
    {
        public async Task<Result<Result>> Handle(Query query, CancellationToken cancellationToken)
        {
            var connection = connector.IsConfigured ? await connector.GetConnectionAsync(cancellationToken) : null;
            return new Result(connector.IsConfigured, connection, Math.Max(0, options.BackfillDays));
        }
    }
}
