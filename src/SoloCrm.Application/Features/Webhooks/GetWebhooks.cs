using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Webhooks;

/// <summary>All webhooks with the outcome of their latest delivery, for the settings (US-18).</summary>
public static class GetWebhooks
{
    public sealed record Query;

    public sealed record Item(
        Guid Id,
        string Name,
        string Url,
        IReadOnlyList<string> Events,
        bool IsActive,
        DateTimeOffset CreatedAt,
        DateTimeOffset? LastAttemptAt,
        bool? LastSucceeded);

    public sealed record Result(IReadOnlyList<Item> Items);

    public sealed class Validator : AbstractValidator<Query>;

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

            var rows = await db.WebhookSubscriptions.AsNoTracking()
                .Select(s => new
                {
                    s.Id,
                    s.Name,
                    s.Url,
                    s.Events,
                    s.IsActive,
                    s.CreatedAt,
                    Last = db.WebhookDeliveries
                        .Where(d => d.SubscriptionId == s.Id)
                        .OrderByDescending(d => d.AttemptedAt)
                        .Select(d => new { d.AttemptedAt, d.Succeeded })
                        .FirstOrDefault(),
                })
                .ToListAsync(cancellationToken);

            // Few rows; sorted here so that "n8n" and "Zapier" sort regardless of case.
            var items = rows
                .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(r => new Item(r.Id, r.Name, r.Url, r.Events, r.IsActive, r.CreatedAt, r.Last?.AttemptedAt, r.Last?.Succeeded))
                .ToList();
            return new Result(items);
        }
    }
}
