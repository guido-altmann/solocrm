using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Common;
using SoloCrm.Domain.Common;

namespace SoloCrm.Application.Features.Contacts;

/// <summary>
/// Replaces the master data of a contact incl. its organization (US-02).
/// </summary>
public static class UpdateContact
{
    public sealed record Command(
        Guid Id,
        string? FirstName,
        string? LastName,
        string? Email,
        string? Phone,
        string? JobTitle,
        string? LinkedInUrl,
        Guid? OrganizationId,
        string? NewOrganizationName,
        LeadSource? Source,
        AddressData? Address) : IContactFields;

    public sealed record Result(Guid Id, Guid? OrganizationId);

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
            var contact = await db.Contacts.SingleOrDefaultAsync(c => c.Id == command.Id, cancellationToken);
            if (contact is null)
            {
                return ContactErrors.NotFound;
            }

            if (await ContactRules.IsEmailTakenAsync(db, command.Email, contact.Id, cancellationToken))
            {
                return ContactErrors.DuplicateEmail;
            }

            var organization = await ContactRules.ResolveOrganizationAsync(db, command, cancellationToken);
            if (organization.IsFailure)
            {
                return organization.Error;
            }

            contact.Update(
                command.FirstName,
                command.LastName,
                command.Email,
                command.Phone,
                command.JobTitle,
                command.LinkedInUrl,
                organization.Value,
                command.Source);
            contact.ChangeAddress(command.Address?.ToAddress() ?? Address.Empty);

            await db.SaveChangesAsync(cancellationToken);

            return new Result(contact.Id, contact.OrganizationId);
        }
    }
}
