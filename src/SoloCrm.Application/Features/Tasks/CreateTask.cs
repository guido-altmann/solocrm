using FluentValidation;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Timeline;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Tasks;

namespace SoloCrm.Application.Features.Tasks;

/// <summary>
/// Creates a follow-up task, from a detail view (link preset) or from „Heute“ (free or linked) (US-11 AK1).
/// </summary>
public static class CreateTask
{
    public sealed record Command(
        string? Title,
        DateOnly? DueDate = null,
        Guid? ContactId = null,
        Guid? OrganizationId = null,
        Guid? OpportunityId = null) : ITaskFields
    {
        public LinkedRecords LinkedTo => new(ContactId, OrganizationId, OpportunityId);
    }

    public sealed record Result(Guid Id);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            Include(new TaskFieldsValidator<Command>());
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
            if (await LinkedRecordRules.CheckAsync(db, command.LinkedTo, cancellationToken) is { } error)
            {
                return error;
            }

            var task = TaskItem.Create(command.Title!, command.DueDate, command.LinkedTo);
            db.Tasks.Add(task);
            await db.SaveChangesAsync(cancellationToken);

            return new Result(task.Id);
        }
    }
}
