using FluentValidation;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Webhooks;

/// <summary>Deletes a webhook together with its delivery log (FK cascade). Pending retries for it are dropped.</summary>
public static class DeleteWebhook
{
    public sealed record Command(Guid Id);

    public sealed record Result(Guid Id);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.Id).NotEmpty();
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

            db.WebhookSubscriptions.Remove(subscription);
            await db.SaveChangesAsync(cancellationToken);

            return new Result(command.Id);
        }
    }
}
