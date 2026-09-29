using FluentValidation;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Tasks;

namespace SoloCrm.Application.Features.Tasks;

/// <summary>Editable fields shared by <see cref="CreateTask"/> and <see cref="UpdateTask"/>.</summary>
public interface ITaskFields
{
    string? Title { get; }

    DateOnly? DueDate { get; }
}

public sealed class TaskFieldsValidator<T> : AbstractValidator<T>
    where T : ITaskFields
{
    public TaskFieldsValidator()
    {
        RuleFor(t => t.Title)
            .Must(title => !string.IsNullOrWhiteSpace(title))
            .WithMessage("Bitte einen Titel angeben.")
            .MaximumLength(TaskItem.TitleMaxLength)
            .WithMessage($"Der Titel darf höchstens {TaskItem.TitleMaxLength} Zeichen lang sein.");
    }
}

public static class TaskErrors
{
    public static Error NotFound { get; } =
        Error.NotFound("Task.NotFound", "Die Aufgabe wurde nicht gefunden.");
}

/// <summary>A task with the names of its linked records (detail views, „Heute“).</summary>
public sealed record TaskSummary(
    Guid Id,
    string Title,
    DateOnly? DueDate,
    DateTimeOffset? CompletedAt,
    RecordRef? Contact,
    RecordRef? Organization,
    RecordRef? Opportunity)
{
    public bool IsCompleted => CompletedAt is not null;

    public bool IsOverdue(DateOnly today) => !IsCompleted && DueDate < today;
}

internal static class TaskQueries
{
    /// <summary>Open tasks by due date (without due date last), then oldest first.</summary>
    public static IQueryable<TaskSummary> ToSummaries(this IQueryable<TaskItem> tasks) => tasks
        .OrderBy(t => t.DueDate == null)
        .ThenBy(t => t.DueDate)
        .ThenBy(t => t.Id)
        .Select(t => new TaskSummary(
            t.Id,
            t.Title,
            t.DueDate,
            t.CompletedAt,
            t.ContactId == null ? null : new RecordRef(t.ContactId.Value, Names.Person(t.Contact!.FirstName, t.Contact.LastName)),
            t.OrganizationId == null ? null : new RecordRef(t.OrganizationId.Value, t.Organization!.Name),
            t.OpportunityId == null ? null : new RecordRef(t.OpportunityId.Value, t.Opportunity!.Title)));
}
