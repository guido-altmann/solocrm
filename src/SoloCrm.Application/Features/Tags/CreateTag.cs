using FluentValidation;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Tags;

namespace SoloCrm.Application.Features.Tags;

/// <summary>
/// Creates a tag with the next palette color (US-15, iteration 4 decision 7). Names are unique regardless of case.
/// </summary>
public static class CreateTag
{
    public sealed record Command(string? Name);

    public sealed record Result(Guid Id, string Name, string Color);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.Name).ValidTagName();
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

            var name = Tag.NormalizeName(command.Name!);
            if (await TagRules.IsNameTakenAsync(db, name, null, cancellationToken))
            {
                return TagErrors.DuplicateName;
            }

            var tag = Tag.Create(name, await TagRules.NextColorAsync(db, cancellationToken));
            db.Tags.Add(tag);
            await db.SaveChangesAsync(cancellationToken);

            return new Result(tag.Id, tag.Name, tag.Color);
        }
    }
}
