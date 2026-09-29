using SoloCrm.Domain.Common;
using SoloCrm.Domain.Contacts;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Domain.Activities;

/// <summary>
/// A note, call, meeting, email or application in the history of a contact, organization or
/// opportunity (SPEC 2.3). Activities can be edited and hard-deleted; they are never archived.
/// </summary>
public sealed class Activity : Entity, IAuditable
{
    public const int SubjectMaxLength = 200;

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

    public Guid? ContactId { get; private set; }

    public Contact? Contact { get; private set; }

    public Guid? OrganizationId { get; private set; }

    public Organization? Organization { get; private set; }

    public Guid? OpportunityId { get; private set; }

    public Opportunity? Opportunity { get; private set; }

    /// <exception cref="ArgumentException">No linked record or blank body.</exception>
    public static Activity Log(ActivityType type, DateTimeOffset occurredAt, string? subject, string body, LinkedRecords linkedTo)
    {
        ArgumentNullException.ThrowIfNull(linkedTo);
        if (!linkedTo.HasAny)
        {
            throw new ArgumentException("An activity requires a contact, an organization or an opportunity.", nameof(linkedTo));
        }

        var activity = new Activity(linkedTo.ContactId, linkedTo.OrganizationId, linkedTo.OpportunityId);
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
