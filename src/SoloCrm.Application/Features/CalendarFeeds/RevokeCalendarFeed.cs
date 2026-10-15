using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.CalendarFeeds;

/// <summary>Switches the ICS feed off (SOL-6); the feed URL is rejected immediately afterwards.</summary>
public static class RevokeCalendarFeed
{
    public sealed record Command;

    public sealed record Result;

    public sealed class Validator : AbstractValidator<Command>;

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
            var feeds = await db.CalendarFeeds.ToListAsync(cancellationToken);
            if (feeds.Count == 0)
            {
                return CalendarFeedErrors.NotFound;
            }

            db.CalendarFeeds.RemoveRange(feeds);
            await db.SaveChangesAsync(cancellationToken);
            return new Result();
        }
    }
}
