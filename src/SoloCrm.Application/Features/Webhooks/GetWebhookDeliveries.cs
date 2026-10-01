using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Webhooks;

/// <summary>The delivery log of one webhook, newest first (US-18 AK3).</summary>
public static class GetWebhookDeliveries
{
    public const int MaxLimit = 200;

    public sealed record Query(Guid SubscriptionId, int Limit = 50);

    public sealed record Item(
        Guid Id,
        Guid EventId,
        string EventType,
        int Attempt,
        int? StatusCode,
        int DurationMs,
        string? Error,
        DateTimeOffset AttemptedAt,
        bool Succeeded);

    public sealed record Result(IReadOnlyList<Item> Items);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(q => q.SubscriptionId).NotEmpty();
            RuleFor(q => q.Limit).InclusiveBetween(1, MaxLimit);
        }
    }

    public sealed class Handler(ICrmDbContextFactory dbFactory, IValidator<Query> validator) : IQueryHandler<Query, Result>
    {
        public async Task<Result<Result>> Handle(Query query, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(query, cancellationToken);
            if (!validation.IsValid)
            {
                return validation.ToValidationError();
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (!await db.WebhookSubscriptions.AnyAsync(s => s.Id == query.SubscriptionId, cancellationToken))
            {
                return WebhookErrors.NotFound;
            }

            var items = await db.WebhookDeliveries.AsNoTracking()
                .Where(d => d.SubscriptionId == query.SubscriptionId)
                .OrderByDescending(d => d.AttemptedAt)
                .ThenByDescending(d => d.Id)
                .Take(query.Limit)
                .Select(d => new Item(d.Id, d.EventId, d.EventType, d.Attempt, d.StatusCode, d.DurationMs, d.Error, d.AttemptedAt, d.Succeeded))
                .ToListAsync(cancellationToken);

            return new Result(items);
        }
    }
}
