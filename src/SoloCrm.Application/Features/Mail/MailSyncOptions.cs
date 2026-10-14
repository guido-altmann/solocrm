namespace SoloCrm.Application.Features.Mail;

/// <summary>Configuration section <c>Mail</c> (ADR-013, <c>docs/email-logging-einrichtung.md</c>).</summary>
public sealed class MailSyncOptions
{
    public const string SectionName = "Mail";

    /// <summary>Days loaded at the first sync of a folder; 0 = only mails from now on.</summary>
    public int BackfillDays { get; set; } = 30;

    /// <summary>
    /// Whether imported emails raise <c>ActivityLogged</c> (and thereby webhooks) like manual activities. Off by
    /// default: a backfill would otherwise send one event per mail to the subscribers.
    /// </summary>
    public bool RaiseActivityLoggedEvents { get; set; }
}
