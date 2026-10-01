using FluentValidation;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Common;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Application.Features.Organizations;

/// <summary>Editable master data shared by <see cref="CreateOrganization"/> and <see cref="UpdateOrganization"/>.</summary>
public interface IOrganizationFields
{
    string? Name { get; }

    OrganizationType Type { get; }

    string? Website { get; }

    /// <summary><c>null</c> counts as an empty address.</summary>
    AddressData? Address { get; }

    string? Notes { get; }
}

public sealed class OrganizationFieldsValidator<T> : AbstractValidator<T>
    where T : IOrganizationFields
{
    public OrganizationFieldsValidator()
    {
        RuleFor(c => c.Name)
            .NotEmpty()
            .WithMessage("Bitte einen Namen angeben.")
            .MaximumLength(Organization.NameMaxLength)
            .WithMessage($"Der Name darf höchstens {Organization.NameMaxLength} Zeichen lang sein.");

        RuleFor(c => c.Type)
            .IsInEnum()
            .WithMessage("Bitte einen gültigen Typ wählen.");

        RuleFor(c => c.Website)
            .Must(BeValidWebsite)
            .WithMessage("Bitte eine gültige Website angeben (z. B. example.com).")
            .When(c => !string.IsNullOrWhiteSpace(c.Website));

        RuleFor(c => c.Address!)
            .SetValidator(new AddressDataValidator())
            .When(c => c.Address is not null);
    }

    private static bool BeValidWebsite(string? website) =>
        Website.TryNormalize(website, out var normalized) && normalized.Length <= Organization.WebsiteMaxLength;
}

public static class OrganizationErrors
{
    public static Error NotFound { get; } =
        Error.NotFound("Organization.NotFound", "Die Organisation wurde nicht gefunden.");
}
