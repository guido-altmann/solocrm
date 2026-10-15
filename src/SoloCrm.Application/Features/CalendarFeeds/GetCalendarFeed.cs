using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.CalendarFeeds;

/// <summary>State of the ICS feed for the settings (SOL-6); the token itself cannot be read back.</summary>
public static class GetCalendarFeed
{
    public sealed record Query;

    public sealed record Result(bool IsEnabled, DateTimeOffset? CreatedAt, DateTimeOffset? LastUsedAt);

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
            var feed = await db.CalendarFeeds.AsNoTracking()
                .Select(f => new Result(true, f.CreatedAt, f.LastUsedAt))
                .SingleOrDefaultAsync(cancellationToken);

            return feed ?? new Result(false, null, null);
        }
    }
}
