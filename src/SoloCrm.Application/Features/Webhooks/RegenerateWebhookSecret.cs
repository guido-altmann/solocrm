using FluentValidation;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Webhooks;

/// <summary>
/// Replaces the signing secret (ADR-010). The new secret is returned once in plaintext; pending retries are
/// signed with it, so the receiver must be updated right away.
/// </summary>
public static class RegenerateWebhookSecret
{
    public sealed record Command(Guid Id);

    public sealed record Result(string Secret);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.Id).NotEmpty();
        }
    }

    public sealed class Handler(ICrmDbContextFactory dbFactory, IValidator<Command> validator, ISecretProtector protector)
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
            var subscription = await db.WebhookSubscriptions.FindAsync([command.Id], cancellationToken);
            if (subscription is null)
            {
                return WebhookErrors.NotFound;
            }

            var secret = WebhookSignature.GenerateSecret();
            subscription.ReplaceSecret(protector.Protect(secret));
            await db.SaveChangesAsync(cancellationToken);

            return new Result(secret);
        }
    }
}
