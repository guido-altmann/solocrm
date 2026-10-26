using SoloCrm.Domain.Common;
using SoloCrm.Domain.Contacts;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Domain.Activities;

/// <summary>
/// A note, call, meeting, email or application in the history of a contact, organization or
/// opportunity (SPEC 2.3). Activities can be edited and hard-deleted; they are never archived.
/// </summary>
public sealed class Activity : Entity, IAuditable, IAuditFieldExclusions
{
    public const int SubjectMaxLength = 200;
    public const int BodyMaxLength = 100_000;

    // Required by EF Core.
    private Activity()
    {
        Body = null!;
    }

    private Activity(Guid? contactId, Guid? organizationId, Guid? opportunityId)
    {
        Body = null!;
        ContactId = contactId;
        OrganizationId = organizationId;
        OpportunityId = opportunityId;
    }

    public ActivityType Type { get; private set; }

    /// <summary>When the activity took place; may lie in the past.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    public string? Subject { get; private set; }

    /// <summary>Markdown; rendered without raw HTML.</summary>
    public string Body { get; private set; }

    /// <summary><see cref="ActivityOrigin.Mail"/> for emails imported from the mailbox (ADR-013).</summary>
    public ActivityOrigin Origin { get; private set; }

    /// <summary>Only set for imported emails.</summary>
    public ActivityDirection? Direction { get; private set; }

    public Guid? ContactId { get; private set; }

    public Contact? Contact { get; private set; }

    public Guid? OrganizationId { get; private set; }

    public Organization? Organization { get; private set; }

    public Guid? OpportunityId { get; private set; }

    public Opportunity? Opportunity { get; private set; }

    /// <exception cref="ArgumentException">No linked record or blank body.</exception>
    public static Activity Log(ActivityType type, DateTimeOffset occurredAt, string? subject, string body, LinkedRecords linkedTo) =>
        Create(type, ActivityOrigin.Manual, null, occurredAt, subject, body, linkedTo);

    /// <summary>Logs an email imported from the mailbox (ADR-013).</summary>
    /// <exception cref="ArgumentException">No linked record or blank body.</exception>
    public static Activity LogMail(ActivityDirection direction, DateTimeOffset occurredAt, string? subject, string body, LinkedRecords linkedTo)
    {
        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown direction.");
        }

        return Create(ActivityType.Email, ActivityOrigin.Mail, direction, occurredAt, subject, body, linkedTo);
    }

    /// <summary>Imported content is stored on the activity itself and must not be duplicated into the audit trail.</summary>
    public bool IsExcludedFromAudit(string fieldName) =>
        Origin == ActivityOrigin.Mail && fieldName is nameof(Subject) or nameof(Body);

    private static Activity Create(
        ActivityType type,
        ActivityOrigin origin,
        ActivityDirection? direction,
        DateTimeOffset occurredAt,
        string? subject,
        string body,
        LinkedRecords linkedTo)
    {
        ArgumentNullException.ThrowIfNull(linkedTo);
        if (!linkedTo.HasAny)
        {
            throw new ArgumentException("An activity requires a contact, an organization or an opportunity.", nameof(linkedTo));
        }

        var activity = new Activity(linkedTo.ContactId, linkedTo.OrganizationId, linkedTo.OpportunityId)
        {
            Origin = origin,
            Direction = direction,
        };
        activity.Update(type, occurredAt, subject, body);
        activity.AddDomainEvent(new ActivityLogged(
            activity.Id,
            activity.Type,
            activity.OccurredAt,
            activity.ContactId,
            activity.OrganizationId,
            activity.OpportunityId));
        return activity;
    }

    /// <summary>Replaces the content; the linked records stay unchanged.</summary>
    /// <exception cref="ArgumentException">Blank body.</exception>
    public void Update(ActivityType type, DateTimeOffset occurredAt, string? subject, string body)
    {
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown activity type.");
        }

        Type = type;
        OccurredAt = occurredAt.ToUniversalTime();
        Subject = string.IsNullOrWhiteSpace(subject) ? null : subject.Trim();
        Body = string.IsNullOrWhiteSpace(body)
            ? throw new ArgumentException("An activity requires a body.", nameof(body))
            : body.Trim();
    }
}
