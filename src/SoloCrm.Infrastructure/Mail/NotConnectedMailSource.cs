using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Mail;

namespace SoloCrm.Infrastructure.Mail;

/// <summary>
/// Used as long as no mailbox is connected: every sync fails with a clear message instead of the handler lacking a
/// source. Replaced by the provider registration (ADR-013).
/// </summary>
internal sealed class NotConnectedMailSource : IMailSource
{
    public string Provider => "None";

    public Task<MailBatch> GetChangesAsync(MailFolder folder, string? continuation, DateTimeOffset? receivedSince, CancellationToken cancellationToken) =>
        throw new MailSourceException("Es ist kein Postfach verbunden.");
}
