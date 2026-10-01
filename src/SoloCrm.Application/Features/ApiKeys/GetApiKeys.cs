using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.ApiKeys;

/// <summary>All API keys for the settings: active ones first, newest first (US-17).</summary>
public static class GetApiKeys
{
    public sealed record Query;

    public sealed record Item(Guid Id, string Name, string Prefix, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, DateTimeOffset? RevokedAt)
    {
        public bool IsRevoked => RevokedAt is not null;

        public string Display => ApiKeyFormat.Display(Prefix);
    }

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
            var items = await db.ApiKeys.AsNoTracking()
                .OrderBy(k => k.RevokedAt != null)
                .ThenByDescending(k => k.CreatedAt)
                .ThenByDescending(k => k.Id)
                .Select(k => new Item(k.Id, k.Name, k.Prefix, k.CreatedAt, k.LastUsedAt, k.RevokedAt))
                .ToListAsync(cancellationToken);

            return new Result(items);
        }
    }
}
