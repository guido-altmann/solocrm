using SoloCrm.Domain.Common;
using SoloCrm.Domain.Contacts;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Domain.Tasks;

/// <summary>
/// A follow-up task, optionally linked to a contact, organization or opportunity (SPEC 2.3).
/// Named <c>TaskItem</c> to avoid clashing with <see cref="System.Threading.Tasks.Task"/>.
/// </summary>
public sealed class TaskItem : Entity, IAuditable
{
    public const int TitleMaxLength = 200;

    // Required by EF Core.
    private TaskItem()
    {
        Title = null!;
    }

    private TaskItem(string title, Guid? contactId, Guid? organizationId, Guid? opportunityId)
    {
        Title = title;
        ContactId = contactId;
        OrganizationId = organizationId;
        OpportunityId = opportunityId;
    }

    public string Title { get; private set; }

    public DateOnly? DueDate { get; private set; }

    /// <summary><c>null</c> while the task is open.</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    public Guid? ContactId { get; private set; }

    public Contact? Contact { get; private set; }

    public Guid? OrganizationId { get; private set; }

    public Organization? Organization { get; private set; }

    public Guid? OpportunityId { get; private set; }

    public Opportunity? Opportunity { get; private set; }

    public bool IsCompleted => CompletedAt is not null;

    /// <exception cref="ArgumentException">Blank title.</exception>
    public static TaskItem Create(string title, DateOnly? dueDate = null, LinkedRecords? linkedTo = null)
    {
        linkedTo ??= LinkedRecords.None;
        var task = new TaskItem(RequireTitle(title), linkedTo.ContactId, linkedTo.OrganizationId, linkedTo.OpportunityId)
        {
            DueDate = dueDate,
        };
        return task;
    }

    /// <exception cref="ArgumentException">Blank title.</exception>
    public void Update(string title, DateOnly? dueDate)
    {
        Title = RequireTitle(title);
        DueDate = dueDate;
    }

    /// <summary>Marks the task as done; completing a completed task changes nothing.</summary>
    public void Complete(DateTimeOffset now)
    {
        if (IsCompleted)
        {
            return;
        }

        CompletedAt = now.ToUniversalTime();
        AddDomainEvent(new TaskCompleted(Id, CompletedAt.Value));
    }

    /// <summary>Opens a completed task again (undo); reopening an open task changes nothing.</summary>
    public void Reopen()
    {
        if (!IsCompleted)
        {
            return;
        }

        CompletedAt = null;
        AddDomainEvent(new TaskReopened(Id));
    }

    /// <summary>Open and due before <paramref name="today"/> (in the configured time zone).</summary>
    public bool IsOverdue(DateOnly today) => !IsCompleted && DueDate < today;

    /// <summary>Open and due on <paramref name="today"/>.</summary>
    public bool IsDueOn(DateOnly today) => !IsCompleted && DueDate == today;

    private static string RequireTitle(string title) =>
        string.IsNullOrWhiteSpace(title)
            ? throw new ArgumentException("A task requires a title.", nameof(title))
            : title.Trim();
}
