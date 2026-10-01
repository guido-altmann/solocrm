using System.Text.Json;
using FluentValidation;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Webhooks;

namespace SoloCrm.Application.Features.Webhooks;

/// <summary>
/// „Test senden“: sends a <c>webhook.ping</c> right away, bypassing the outbox, and records the attempt in the
/// delivery log. Works for inactive webhooks too, so a target can be checked before it is switched on.
/// </summary>
public static class SendWebhookPing
{
    public sealed record Command(Guid Id);

    public sealed record Result(bool Succeeded, int? StatusCode, int DurationMs, string? Error);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.Id).NotEmpty();
        }
    }

    public sealed class Handler(
        ICrmDbContextFactory dbFactory,
        IValidator<Command> validator,
        ISecretProtector protector,
        IWebhookSender sender,
        TimeProvider timeProvider) : ICommandHandler<Command, Result>
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

            var now = timeProvider.GetUtcNow();
            var data = JsonSerializer.Serialize(new { subscriptionId = subscription.Id });
            var ping = new WebhookEvent(Guid.CreateVersion7(), WebhookEvents.Ping, now, data);

            var sent = await sender.SendAsync(subscription.Url, protector.Unprotect(subscription.ProtectedSecret), ping, cancellationToken);

            db.WebhookDeliveries.Add(WebhookDelivery.Record(
                subscription.Id, ping.Id, ping.Type, 1, sent.StatusCode, sent.DurationMs, sent.Error, now));
            await db.SaveChangesAsync(cancellationToken);

            return new Result(sent.Succeeded, sent.StatusCode, sent.DurationMs, sent.Error);
        }
    }
}
