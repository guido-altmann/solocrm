using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Common;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Contacts;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Application.Features.Contacts;

/// <summary>Editable master data shared by <see cref="CreateContact"/> and <see cref="UpdateContact"/>.</summary>
public interface IContactFields
{
    string? FirstName { get; }

    string? LastName { get; }

    string? Email { get; }

    string? Phone { get; }

    string? JobTitle { get; }

    string? LinkedInUrl { get; }

    /// <summary>Existing organization to assign.</summary>
    Guid? OrganizationId { get; }

    /// <summary>Creates an organization of type <c>Other</c> with this name in the same transaction (US-02 AK2).</summary>
    string? NewOrganizationName { get; }

    LeadSource? Source { get; }

    /// <summary><c>null</c> counts as an empty address.</summary>
    AddressData? Address { get; }
}

public sealed class ContactFieldsValidator<T> : AbstractValidator<T>
    where T : IContactFields
{
    public ContactFieldsValidator()
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
            .OverridePropertyName(nameof(IContactFields.Email))
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
            .OverridePropertyName(nameof(IContactFields.LinkedInUrl))
            .When(c => !string.IsNullOrWhiteSpace(c.LinkedInUrl));

        RuleFor(c => c.NewOrganizationName)
            .Empty()
            .WithMessage("Bitte entweder eine bestehende Organisation wählen oder eine neue anlegen.")
            .When(c => c.OrganizationId is not null);

        RuleFor(c => c.NewOrganizationName!.Trim())
            .MaximumLength(Organization.NameMaxLength)
            .WithMessage($"Der Name der Organisation darf höchstens {Organization.NameMaxLength} Zeichen lang sein.")
            .OverridePropertyName(nameof(IContactFields.NewOrganizationName))
            .When(c => !string.IsNullOrWhiteSpace(c.NewOrganizationName));

        RuleFor(c => c.Source)
            .IsInEnum()
            .WithMessage("Bitte eine gültige Quelle wählen.");

        RuleFor(c => c.Address!)
            .SetValidator(new AddressDataValidator())
            .When(c => c.Address is not null);
    }

    private static bool BeAbsoluteHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

public static class ContactErrors
{
    public static Error NotFound { get; } =
        Error.NotFound("Contact.NotFound", "Der Kontakt wurde nicht gefunden.");

    public static Error DuplicateEmail { get; } =
        Error.Conflict("Contact.DuplicateEmail", "Ein Kontakt mit dieser E-Mail-Adresse existiert bereits.");

    public static Error OrganizationNotFound { get; } =
        Error.NotFound("Contact.OrganizationNotFound", "Die gewählte Organisation existiert nicht.");

    /// <summary>The form field a business error belongs to, or <c>null</c>.</summary>
    public static string? FieldOf(Error error) =>
        error == DuplicateEmail ? nameof(IContactFields.Email)
        : error == OrganizationNotFound ? nameof(IContactFields.OrganizationId)
        : null;
}

internal static class ContactRules
{
    /// <summary>
    /// Resolves the organization of a contact: checks that an existing one exists, or adds a new one
    /// (type <c>Other</c>) to <paramref name="db"/> so it is saved together with the contact.
    /// </summary>
    public static async Task<Result<Guid?>> ResolveOrganizationAsync(
        ICrmDbContext db,
        IContactFields fields,
        CancellationToken cancellationToken)
    {
        if (fields.OrganizationId is { } organizationId)
        {
            return await db.Organizations.AnyAsync(o => o.Id == organizationId, cancellationToken)
                ? organizationId
                : ContactErrors.OrganizationNotFound;
        }

        if (!string.IsNullOrWhiteSpace(fields.NewOrganizationName))
        {
            var organization = Organization.Create(fields.NewOrganizationName);
            db.Organizations.Add(organization);
            return organization.Id;
        }

        return Result<Guid?>.Success(null);
    }

    /// <summary>
    /// The email column is case-insensitive (citext), so this comparison is too.
    /// The unique index remains the safety net for concurrent writes.
    /// </summary>
    public static Task<bool> IsEmailTakenAsync(ICrmDbContext db, string? email, Guid? exceptContactId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Task.FromResult(false);
        }

        var normalized = email.Trim();
        return db.Contacts.AnyAsync(c => c.Email == normalized && c.Id != exceptContactId, cancellationToken);
    }
}
