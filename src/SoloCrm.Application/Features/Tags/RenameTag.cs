using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Tags;

namespace SoloCrm.Application.Features.Tags;

/// <summary>
/// Renames a tag in the settings (US-15). Changing only the case („kunde“ → „Kunde“) is allowed.
/// </summary>
public static class RenameTag
{
    public sealed record Command(Guid Id, string? Name);

    public sealed record Result(Guid Id, string Name);

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
            var tag = await db.Tags.SingleOrDefaultAsync(t => t.Id == command.Id, cancellationToken);
            if (tag is null)
            {
                return TagErrors.NotFound;
            }

            var name = Tag.NormalizeName(command.Name!);
            if (await TagRules.IsNameTakenAsync(db, name, tag.Id, cancellationToken))
            {
                return TagErrors.DuplicateName;
            }

            tag.Rename(name);
            await db.SaveChangesAsync(cancellationToken);

            return new Result(tag.Id, tag.Name);
        }
    }
}
