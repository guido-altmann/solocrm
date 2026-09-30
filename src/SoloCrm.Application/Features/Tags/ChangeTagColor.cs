using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Tags;

namespace SoloCrm.Application.Features.Tags;

/// <summary>Changes the color of a tag to another color of the palette (US-15, iteration 4 decision 7).</summary>
public static class ChangeTagColor
{
    public sealed record Command(Guid Id, string? Color);

    public sealed record Result(Guid Id, string Color);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.Color)
                .Must(TagPalette.Contains)
                .WithMessage("Bitte eine Farbe aus der Palette wählen.");
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
            var tag = await db.Tags.SingleOrDefaultAsync(t => t.Id == command.Id, cancellationToken);
            if (tag is null)
            {
                return TagErrors.NotFound;
            }

            tag.ChangeColor(command.Color!);
            await db.SaveChangesAsync(cancellationToken);

            return new Result(tag.Id, tag.Color);
        }
    }
}
