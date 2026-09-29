using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Opportunities;

namespace SoloCrm.Application.Features.Stages;

/// <summary>
/// Adds a stage at the end of the pipeline (US-09).
/// </summary>
public static class CreateStage
{
    public sealed record Command(string? Name, StageStatus Status = StageStatus.Open);

    public sealed record Result(Guid Id);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.Name).ValidStageName();
            RuleFor(c => c.Status).IsInEnum().WithMessage("Bitte einen gültigen Status wählen.");
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
            var lastSortOrder = await db.Stages.MaxAsync(s => (int?)s.SortOrder, cancellationToken) ?? 0;

            var stage = Stage.Create(command.Name!, lastSortOrder + 1, command.Status);
            db.Stages.Add(stage);
            await db.SaveChangesAsync(cancellationToken);

            return new Result(stage.Id);
        }
    }
}
