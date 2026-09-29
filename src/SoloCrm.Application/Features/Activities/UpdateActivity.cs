using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Activities;

namespace SoloCrm.Application.Features.Activities;

/// <summary>
/// Edits the content of an activity; the linked records stay unchanged (iteration 3, decision 3).
/// </summary>
public static class UpdateActivity
{
    public sealed record Command(
        Guid Id,
        ActivityType Type,
        DateTimeOffset? OccurredAt,
        string? Subject,
        string? Body) : IActivityFields;

    public sealed record Result(Guid Id);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator(AppClock clock)
        {
            Include(new ActivityFieldsValidator<Command>(clock));

            RuleFor(c => c.OccurredAt)
                .NotNull()
                .WithMessage("Bitte einen Zeitpunkt angeben.");
        }
    }

    public sealed class Handler(ICrmDbContextFactory dbFactory, IValidator<Command> validator)
        : ICommandHandler<Command, Result>
    {
        public async Task<Result<Result>> Handle(Command command, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(command, cancellationToken);
            if (!validation.IsValid)
            {
                return validation.ToValidationError();
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var activity = await db.Activities.SingleOrDefaultAsync(a => a.Id == command.Id, cancellationToken);
            if (activity is null)
            {
                return ActivityErrors.NotFound;
            }

            activity.Update(command.Type, command.OccurredAt!.Value, command.Subject, command.Body!);
            await db.SaveChangesAsync(cancellationToken);

            return new Result(activity.Id);
        }
    }
}
