using FluentValidation;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Common;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Contacts;

namespace SoloCrm.Application.Features.Contacts;

/// <summary>
/// Creates a contact; only a first or last name is required (US-01). Optionally assigns an existing
/// or a newly created organization (US-02).
/// </summary>
public static class CreateContact
{
    public sealed record Command(
        string? FirstName,
        string? LastName,
        string? Email = null,
        string? Phone = null,
        string? JobTitle = null,
        string? LinkedInUrl = null,
        Guid? OrganizationId = null,
        string? NewOrganizationName = null,
        LeadSource? Source = null,
        AddressData? Address = null) : IContactFields;

    public sealed record Result(Guid Id);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            Include(new ContactFieldsValidator<Command>());
        }
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

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            if (await ContactRules.IsEmailTakenAsync(db, command.Email, null, cancellationToken))
            {
                return ContactErrors.DuplicateEmail;
            }

            var organization = await ContactRules.ResolveOrganizationAsync(db, command, cancellationToken);
            if (organization.IsFailure)
            {
                return organization.Error;
            }

            var contact = Contact.Create(
                command.FirstName,
                command.LastName,
                command.Email,
                command.Phone,
                command.JobTitle,
                command.LinkedInUrl,
                organization.Value,
                command.Source,
                command.Address?.ToAddress());

            db.Contacts.Add(contact);
            await db.SaveChangesAsync(cancellationToken);

            return new Result(contact.Id);
        }
    }
}
