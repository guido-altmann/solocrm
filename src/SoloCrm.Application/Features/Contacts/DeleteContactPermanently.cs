using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Contacts;

/// <summary>
/// GDPR erasure of a contact (US-20): deletes the contact with its activities and tasks (also those that additionally
/// reference an organization or opportunity, iteration 6 decision 2) and its tag assignments in one transaction.
/// Opportunities stay and lose their primary contact. The audit interceptor writes <c>Deleted</c> without field
/// values and anonymizes the history (ADR-006); <c>ContactDeleted</c> carries only the id. Not offered via the REST
/// API (decision 5).
/// </summary>
public static partial class DeleteContactPermanently
{
    /// <param name="Confirmation">The name of the contact as typed by the user (decision 6).</param>
    public sealed record Command(Guid ContactId, string Confirmation);

    public sealed record Result(Guid ContactId, int DeletedActivities, int DeletedTasks, int ClearedOpportunities);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.Confirmation)
                .NotEmpty()
                .WithMessage("Bitte zur Bestätigung den Namen des Kontakts eingeben.");
        }
    }

    public sealed partial class Handler(ICrmDbContextFactory dbFactory, IValidator<Command> validator, ILogger<Handler> logger)
        : ICommandHandler<Command, Result>
    {
        public async Task<Result<Result>> Handle(Command command, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(command, cancellationToken);
            if (!validation.IsValid)
            {
                return validation.ToValidationError();
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var contact = await db.Contacts.SingleOrDefaultAsync(c => c.Id == command.ContactId, cancellationToken);
            if (contact is null)
            {
                return ContactErrors.NotFound;
            }

            if (!IsConfirmed(command.Confirmation, Names.Person(contact.FirstName, contact.LastName)))
            {
                return ContactErrors.ErasureNotConfirmed;
            }

            // Loaded explicitly instead of relying on the database cascade, so the audit interceptor sees them.
            var activities = await db.Activities.Where(a => a.ContactId == contact.Id).ToListAsync(cancellationToken);
            var tasks = await db.Tasks.Where(t => t.ContactId == contact.Id).ToListAsync(cancellationToken);
            var opportunities = await db.Opportunities.CountAsync(o => o.PrimaryContactId == contact.Id, cancellationToken);

            db.Activities.RemoveRange(activities);
            db.Tasks.RemoveRange(tasks);
            contact.Erase();
            db.Contacts.Remove(contact);

            // Tag assignments cascade and opportunities are set to no primary contact by the database.
            await db.SaveChangesAsync(cancellationToken);
            LogErased(logger, contact.Id, activities.Count, tasks.Count);

            return new Result(contact.Id, activities.Count, tasks.Count, opportunities);
        }

        // Only the id: the log must not contain personal data (SPEC 6).
        [LoggerMessage(Level = LogLevel.Information, Message = "Contact {ContactId} erased with {Activities} activities and {Tasks} tasks")]
        private static partial void LogErased(ILogger logger, Guid contactId, int activities, int tasks);

        /// <summary>Like GitHub: the exact name, ignoring surrounding and repeated whitespace.</summary>
        private static bool IsConfirmed(string confirmation, string name) =>
            string.Equals(string.Join(' ', confirmation.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)), name, StringComparison.Ordinal);
    }
}
