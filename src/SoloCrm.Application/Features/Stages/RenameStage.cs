using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Stages;

/// <summary>
/// Renames a stage (US-09 AK1).
/// </summary>
public static class RenameStage
{
    public sealed record Command(Guid Id, string? Name);

    public sealed record Result(Guid Id);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.Name).ValidStageName();
        }
    }

    public sealed class Handler(ICrmDbContextFactory dbFactory, IValidator<Command> validator) : ICommandHandler<Command, Result>
    {
        public async Task<Result<Result>> Handle(Command command, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(command, cancellationToken);
            if (!validation.IsValid)
            {
                return validation.ToValidationError();
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var stage = await db.Stages.SingleOrDefaultAsync(s => s.Id == command.Id, cancellationToken);
            if (stage is null)
            {
                return StageErrors.NotFound;
            }

            stage.Rename(command.Name!);
            await db.SaveChangesAsync(cancellationToken);

            return new Result(stage.Id);
        }
    }
}
