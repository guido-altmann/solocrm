using FluentValidation;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Timeline;
using SoloCrm.Domain.Activities;
using SoloCrm.Domain.Common;

namespace SoloCrm.Application.Features.Activities;

/// <summary>
/// Records a note, call, meeting, email or application for a contact, organization and/or opportunity (US-10).
/// <c>OccurredAt</c> defaults to now and can be backdated (AK1). Raises <c>ActivityLogged</c>.
/// </summary>
public static class LogActivity
{
    public sealed record Command(
        ActivityType Type,
        string? Body,
        string? Subject = null,
        DateTimeOffset? OccurredAt = null,
        Guid? ContactId = null,
        Guid? OrganizationId = null,
        Guid? OpportunityId = null) : IActivityFields
    {
        public LinkedRecords LinkedTo => new(ContactId, OrganizationId, OpportunityId);
    }

    public sealed record Result(Guid Id);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator(AppClock clock)
        {
            Include(new ActivityFieldsValidator<Command>(clock));

            RuleFor(c => c.LinkedTo)
                .Must(linkedTo => linkedTo.HasAny)
                .WithMessage(LinkedRecordErrors.Required.Message);
        }
    }

    public sealed class Handler(ICrmDbContextFactory dbFactory, IValidator<Command> validator, AppClock clock)
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
            if (await LinkedRecordRules.CheckAsync(db, command.LinkedTo, cancellationToken) is { } error)
            {
                return error;
            }

            var activity = Activity.Log(
                command.Type,
                command.OccurredAt ?? clock.UtcNow,
                command.Subject,
                command.Body!,
                command.LinkedTo);

            db.Activities.Add(activity);
            await db.SaveChangesAsync(cancellationToken);

            return new Result(activity.Id);
        }
    }
}
