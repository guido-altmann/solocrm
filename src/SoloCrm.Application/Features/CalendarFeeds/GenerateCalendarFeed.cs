using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.CalendarFeeds;

namespace SoloCrm.Application.Features.CalendarFeeds;

/// <summary>
/// Creates the token for the ICS feed of the tasks (SOL-6) or replaces the existing one, which makes the old
/// feed URL invalid. The token is returned once in plaintext; only its hash is stored.
/// </summary>
public static class GenerateCalendarFeed
{
    public sealed record Command;

    public sealed record Result(string Token);

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

            var generated = CalendarFeedToken.Generate();

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            db.CalendarFeeds.RemoveRange(await db.CalendarFeeds.ToListAsync(cancellationToken));
            db.CalendarFeeds.Add(CalendarFeed.Create(generated.Hash));
            await db.SaveChangesAsync(cancellationToken);

            return new Result(generated.Token);
        }
    }
}
