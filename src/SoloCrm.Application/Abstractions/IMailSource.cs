using SoloCrm.Domain.Mail;

namespace SoloCrm.Application.Abstractions;

/// <summary>
/// A mailbox the CRM reads emails from (ADR-013). Implemented per provider in Infrastructure; the sync use case
/// only depends on this abstraction. Sources are read-only.
/// </summary>
public interface IMailSource
{
    /// <summary>Stable name of the provider, stored with every imported message (e.g. <c>Graph</c>).</summary>
    string Provider { get; }

    /// <summary>
    /// Returns one page of changes of <paramref name="folder"/>. <paramref name="continuation"/> is either the delta
    /// token of the previous sync or the <see cref="MailBatch.NextPageToken"/> of the previous page; without it the
    /// source starts with the messages since <paramref name="receivedSince"/>.
    /// </summary>
    /// <exception cref="MailDeltaExpiredException">The delta token is no longer valid; sync from scratch.</exception>
    /// <exception cref="MailSourceException">The mailbox cannot be read (not connected, denied, unreachable).</exception>
    Task<MailBatch> GetChangesAsync(MailFolder folder, string? continuation, DateTimeOffset? receivedSince, CancellationToken cancellationToken);
}

/// <summary>
/// One page of messages. While <see cref="NextPageToken"/> is set, more pages follow; the last page carries the
/// <see cref="DeltaToken"/> for the next sync.
/// </summary>
public sealed record MailBatch(IReadOnlyList<IncomingMail> Messages, string? NextPageToken, string? DeltaToken);

/// <summary>A message as delivered by a source. Addresses are plain email addresses (no display names).</summary>
/// <param name="ExternalId">Id at the provider.</param>
/// <param name="InternetMessageId">RFC 5322 <c>Message-ID</c>; identifies the message across folders.</param>
/// <param name="Folder">The folder it was read from; decides the direction.</param>
/// <param name="Sender">Sender address.</param>
/// <param name="Recipients">To recipients first, then Cc.</param>
/// <param name="OccurredAt">Received time (Inbox) or sent time (Sent).</param>
/// <param name="Body">Body as delivered; HTML if <paramref name="BodyIsHtml"/>.</param>
public sealed record IncomingMail(
    string ExternalId,
    string? InternetMessageId,
    MailFolder Folder,
    string? Sender,
    IReadOnlyList<string> Recipients,
    string? Subject,
    DateTimeOffset OccurredAt,
    string? Body,
    bool BodyIsHtml);

/// <summary>The mailbox cannot be read. The message is shown to the user and therefore German.</summary>
public class MailSourceException : Exception
{
    public MailSourceException()
    {
    }

    public MailSourceException(string message)
        : base(message)
    {
    }

    public MailSourceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The provider no longer accepts the stored delta token.</summary>
public sealed class MailDeltaExpiredException : MailSourceException
{
    public MailDeltaExpiredException()
        : base("Der Abgleich-Token ist abgelaufen.")
    {
    }

    public MailDeltaExpiredException(string message)
        : base(message)
    {
    }

    public MailDeltaExpiredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
