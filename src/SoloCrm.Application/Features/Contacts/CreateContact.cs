using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Contacts;

namespace SoloCrm.Application.Features.Contacts;

/// <summary>
/// Creates a contact; only a first or last name is required (US-01).
/// </summary>
public static class CreateContact
{
    public sealed record Command(
        string? FirstName,
        string? LastName,
        string? Email = null,
        string? Phone = null,
        string? JobTitle = null,
        string? LinkedInUrl = null);

    public sealed record Result(Guid Id);

    public static class Errors
    {
        public static Error DuplicateEmail { get; } =
            Error.Conflict("Contact.DuplicateEmail", "Ein Kontakt mit dieser E-Mail-Adresse existiert bereits.");
    }

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.LastName)
                .Must((command, _) => !string.IsNullOrWhiteSpace(command.FirstName) || !string.IsNullOrWhiteSpace(command.LastName))
                .WithMessage("Bitte Vor- oder Nachnamen angeben.");

            RuleFor(c => c.FirstName)
                .MaximumLength(Contact.FirstNameMaxLength)
                .WithMessage($"Der Vorname darf höchstens {Contact.FirstNameMaxLength} Zeichen lang sein.");

            RuleFor(c => c.LastName)
                .MaximumLength(Contact.LastNameMaxLength)
                .WithMessage($"Der Nachname darf höchstens {Contact.LastNameMaxLength} Zeichen lang sein.");

            RuleFor(c => c.Email!.Trim())
                .MaximumLength(Contact.EmailMaxLength)
                .WithMessage($"Die E-Mail-Adresse darf höchstens {Contact.EmailMaxLength} Zeichen lang sein.")
                .EmailAddress()
                .WithMessage("Bitte eine gültige E-Mail-Adresse angeben.")
                .OverridePropertyName(nameof(Command.Email))
                .When(c => !string.IsNullOrWhiteSpace(c.Email));

            RuleFor(c => c.Phone)
                .MaximumLength(Contact.PhoneMaxLength)
                .WithMessage($"Die Telefonnummer darf höchstens {Contact.PhoneMaxLength} Zeichen lang sein.");

            RuleFor(c => c.JobTitle)
                .MaximumLength(Contact.JobTitleMaxLength)
                .WithMessage($"Die Rolle darf höchstens {Contact.JobTitleMaxLength} Zeichen lang sein.");

            RuleFor(c => c.LinkedInUrl!.Trim())
                .MaximumLength(Contact.LinkedInUrlMaxLength)
                .WithMessage($"Die LinkedIn-URL darf höchstens {Contact.LinkedInUrlMaxLength} Zeichen lang sein.")
                .Must(BeAbsoluteHttpUrl)
                .WithMessage("Bitte eine gültige URL (http/https) angeben.")
                .OverridePropertyName(nameof(Command.LinkedInUrl))
                .When(c => !string.IsNullOrWhiteSpace(c.LinkedInUrl));
        }

        private static bool BeAbsoluteHttpUrl(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    public sealed class Handler(ICrmDbContextFactory dbFactory, IValidator<Command> validator)
        : ICommandHandler<Command, Result>
    {
        public async Task<Result<Result>> Handle(Command command, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(command, cancellationToken);
            if (!validation.IsValid)
            {
                return validation.ToValidationError();
            }

            var contact = Contact.Create(
                command.FirstName,
                command.LastName,
                command.Email,
                command.Phone,
                command.JobTitle,
                command.LinkedInUrl);

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            // The email column is case-insensitive (citext), so this comparison is too.
            // The unique index remains the safety net for concurrent inserts.
            if (contact.Email is not null
                && await db.Contacts.AnyAsync(c => c.Email == contact.Email, cancellationToken))
            {
                return Errors.DuplicateEmail;
            }

            db.Contacts.Add(contact);
            await db.SaveChangesAsync(cancellationToken);

            return new Result(contact.Id);
        }
    }
}
