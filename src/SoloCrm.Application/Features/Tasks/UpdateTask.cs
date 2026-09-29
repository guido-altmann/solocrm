using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Tasks;

/// <summary>Changes title and due date of a task; the linked records stay unchanged.</summary>
public static class UpdateTask
{
    public sealed record Command(Guid Id, string? Title, DateOnly? DueDate) : ITaskFields;

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
            var task = await db.Tasks.SingleOrDefaultAsync(t => t.Id == command.Id, cancellationToken);
            if (task is null)
            {
                return TaskErrors.NotFound;
            }

            task.Update(command.Title!, command.DueDate);
            await db.SaveChangesAsync(cancellationToken);

            return new Result(task.Id);
        }
    }
}
