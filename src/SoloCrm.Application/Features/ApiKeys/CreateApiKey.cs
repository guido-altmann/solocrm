using FluentValidation;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.ApiKeys;

namespace SoloCrm.Application.Features.ApiKeys;

/// <summary>Creates an API key (US-17 AK1). The key is returned once in plaintext; only its hash is stored.</summary>
public static class CreateApiKey
{
    public sealed record Command(string? Name);

    public sealed record Result(Guid Id, string Key, string Prefix);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.Name)
                .NotEmpty()
                .WithMessage("Bitte einen Namen angeben, z. B. „n8n“.")
                .Must(name => name is null || name.Trim().Length <= ApiKey.NameMaxLength)
                .WithMessage($"Der Name darf höchstens {ApiKey.NameMaxLength} Zeichen lang sein.");
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

            var generated = ApiKeyFormat.Generate();
            var apiKey = ApiKey.Create(command.Name!, generated.Prefix, generated.Hash);

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            db.ApiKeys.Add(apiKey);
            await db.SaveChangesAsync(cancellationToken);

            return new Result(apiKey.Id, generated.Key, generated.Prefix);
        }
    }
}
