using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.ApiKeys;

/// <summary>
/// Checks an <c>X-Api-Key</c> header value (US-17 AK2): malformed keys are rejected without a database query, the
/// hash is compared in constant time and <c>LastUsedAt</c> is updated at most once per minute.
/// </summary>
public static class AuthenticateApiKey
{
    public sealed record Command(string? Key);

    public sealed record Result(Guid ApiKeyId, string Name);

    public sealed class Validator : AbstractValidator<Command>;

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

            if (!ApiKeyFormat.TryGetPrefix(command.Key, out var prefix))
            {
                return ApiKeyErrors.Invalid;
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var apiKey = await db.ApiKeys.SingleOrDefaultAsync(k => k.Prefix == prefix, cancellationToken);
            if (apiKey is null || apiKey.IsRevoked || !ApiKeyFormat.HashesEqual(apiKey.KeyHash, ApiKeyFormat.Hash(command.Key!)))
            {
                return ApiKeyErrors.Invalid;
            }

            if (apiKey.RecordUse(timeProvider.GetUtcNow()))
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            return new Result(apiKey.Id, apiKey.Name);
        }
    }
}
