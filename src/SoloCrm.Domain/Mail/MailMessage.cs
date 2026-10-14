using SoloCrm.Domain.Activities;
using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.Mail;

/// <summary>
/// Links an imported email to the activity that was created for it. It prevents duplicates (the same message in
/// Inbox and Sent, or delivered twice) and holds no content; subject and text live on the activity (ADR-013).
/// Deleting the activity (e.g. with its contact, US-20) deletes the link.
/// </summary>
public sealed class MailMessage : Entity
{
    public const int ProviderMaxLength = 20;
    public const int ExternalIdMaxLength = 512;
    public const int InternetMessageIdMaxLength = 998;

    // Required by EF Core.
    private MailMessage()
    {
        Provider = null!;
        ExternalId = null!;
        InternetMessageId = null!;
    }

    public MailMessage(Guid activityId, string provider, string externalId, string internetMessageId, MailFolder folder)
    {
        if (activityId == Guid.Empty)
        {
            throw new ArgumentException("An activity id is required.", nameof(activityId));
        }

        ActivityId = activityId;
        Provider = Required(provider, ProviderMaxLength, nameof(provider));
        ExternalId = Required(externalId, ExternalIdMaxLength, nameof(externalId));
        InternetMessageId = Required(internetMessageId, InternetMessageIdMaxLength, nameof(internetMessageId));
        Folder = Enum.IsDefined(folder) ? folder : throw new ArgumentOutOfRangeException(nameof(folder), folder, "Unknown folder.");
    }

    public Guid ActivityId { get; private set; }

    public Activity? Activity { get; private set; }

    /// <summary>Name of the mail source, e.g. <c>Graph</c>.</summary>
    public string Provider { get; private set; }

    /// <summary>Id of the message at the provider; only valid for that provider and folder.</summary>
    public string ExternalId { get; private set; }

    /// <summary>The RFC 5322 <c>Message-ID</c>; identifies a message across folders.</summary>
    public string InternetMessageId { get; private set; }

    public MailFolder Folder { get; private set; }

    private static string Required(string value, int maxLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", name);
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : throw new ArgumentException($"At most {maxLength} characters are allowed.", name);
    }
}
