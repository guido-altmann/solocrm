using FluentValidation;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Activities;

namespace SoloCrm.Application.Features.Activities;

/// <summary>Editable content shared by <see cref="LogActivity"/> and <see cref="UpdateActivity"/>.</summary>
public interface IActivityFields
{
    ActivityType Type { get; }

    /// <summary><c>null</c> means now.</summary>
    DateTimeOffset? OccurredAt { get; }

    string? Subject { get; }

    /// <summary>Markdown.</summary>
    string? Body { get; }
}

public sealed class ActivityFieldsValidator<T> : AbstractValidator<T>
    where T : IActivityFields
{
    /// <summary>Tolerance for clock differences; activities are backdated, not scheduled (tasks are for the future).</summary>
    public static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    public ActivityFieldsValidator(AppClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        RuleFor(a => a.Type)
            .IsInEnum()
            .WithMessage("Bitte eine gültige Art wählen.");

        RuleFor(a => a.OccurredAt)
            .Must(occurredAt => occurredAt <= clock.UtcNow + FutureTolerance)
            .WithMessage("Der Zeitpunkt darf nicht in der Zukunft liegen.")
            .When(a => a.OccurredAt is not null);

        RuleFor(a => a.Subject)
            .MaximumLength(Activity.SubjectMaxLength)
            .WithMessage($"Der Betreff darf höchstens {Activity.SubjectMaxLength} Zeichen lang sein.");

        RuleFor(a => a.Body)
            .Must(body => !string.IsNullOrWhiteSpace(body))
            .WithMessage("Bitte einen Text eingeben.");
    }
}

public static class ActivityErrors
{
    public static Error NotFound { get; } =
        Error.NotFound("Activity.NotFound", "Die Aktivität wurde nicht gefunden.");
}
