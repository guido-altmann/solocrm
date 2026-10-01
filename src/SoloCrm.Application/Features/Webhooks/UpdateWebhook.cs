using FluentValidation;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Webhooks;

/// <summary>Changes name, URL, events and the active flag of a webhook (US-18 AK1). The secret stays.</summary>
public static class UpdateWebhook
{
    public sealed record Command(Guid Id, string? Name, string? Url, IReadOnlyList<string>? Events, bool IsActive);

    public sealed record Result(Guid Id);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator(WebhookTargets targets)
        {
            RuleFor(c => c.Id).NotEmpty();
            RuleFor(c => c.Name).ValidWebhookName();
            RuleFor(c => c.Url).ValidWebhookUrl(targets);
            RuleFor(c => c.Events).ValidWebhookEvents();
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
            var subscription = await db.WebhookSubscriptions.FindAsync([command.Id], cancellationToken);
            if (subscription is null)
            {
                return WebhookErrors.NotFound;
            }

            subscription.Update(command.Name!, command.Url!, command.Events!, command.IsActive);
            await db.SaveChangesAsync(cancellationToken);

            return new Result(subscription.Id);
        }
    }
}
