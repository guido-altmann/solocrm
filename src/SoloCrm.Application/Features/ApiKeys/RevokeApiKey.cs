using FluentValidation;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.ApiKeys;

/// <summary>Revokes an API key; it is rejected immediately afterwards. Revoking again changes nothing.</summary>
public static class RevokeApiKey
{
    public sealed record Command(Guid Id);

    public sealed record Result(Guid Id, DateTimeOffset RevokedAt);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.Id).NotEmpty();
        }
    }

    public sealed class Handler(ICrmDbContextFactory dbFactory, IValidator<Command> validator, TimeProvider timeProvider)
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
            var apiKey = await db.ApiKeys.FindAsync([command.Id], cancellationToken);
            if (apiKey is null)
            {
                return ApiKeyErrors.NotFound;
            }

            apiKey.Revoke(timeProvider.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);

            return new Result(apiKey.Id, apiKey.RevokedAt!.Value);
        }
    }
}
