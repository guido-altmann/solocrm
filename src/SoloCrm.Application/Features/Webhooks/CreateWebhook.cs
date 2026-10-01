using FluentValidation;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Webhooks;

namespace SoloCrm.Application.Features.Webhooks;

/// <summary>
/// Creates an active webhook subscription with a generated signing secret (US-18 AK1). The secret is returned
/// once in plaintext and stored encrypted (ADR-010).
/// </summary>
public static class CreateWebhook
{
    public sealed record Command(string? Name, string? Url, IReadOnlyList<string>? Events);

    public sealed record Result(Guid Id, string Secret);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator(WebhookTargets targets)
        {
            RuleFor(c => c.Name).ValidWebhookName();
            RuleFor(c => c.Url).ValidWebhookUrl(targets);
            RuleFor(c => c.Events).ValidWebhookEvents();
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

            var secret = WebhookSignature.GenerateSecret();
            var subscription = WebhookSubscription.Create(command.Name!, command.Url!, command.Events!, protector.Protect(secret));

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            db.WebhookSubscriptions.Add(subscription);
            await db.SaveChangesAsync(cancellationToken);

            return new Result(subscription.Id, secret);
        }
    }
}
