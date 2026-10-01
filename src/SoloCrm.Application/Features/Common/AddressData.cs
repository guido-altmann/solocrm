using FluentValidation;
using SoloCrm.Domain.Common;

namespace SoloCrm.Application.Features.Common;

/// <summary>Address fields in commands and results (iteration 5 decision 14); maps to the value object <see cref="Address"/>.</summary>
public sealed record AddressData(
    string? Street = null,
    string? Street2 = null,
    string? PostalCode = null,
    string? City = null,
    string? Region = null,
    string? CountryCode = null)
{
    public static AddressData Empty { get; } = new();

    public static AddressData From(Address address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return new AddressData(address.Street, address.Street2, address.PostalCode, address.City, address.Region, address.CountryCode);
    }

    /// <summary>Call only after validation; invalid values throw like the value object.</summary>
    public Address ToAddress() => Address.Create(Street, Street2, PostalCode, City, Region, CountryCode);
}

public sealed class AddressDataValidator : AbstractValidator<AddressData>
{
    public AddressDataValidator()
    {
        RuleFor(a => a.Street!.Trim())
            .MaximumLength(Address.StreetMaxLength)
            .WithMessage($"Die Straße darf höchstens {Address.StreetMaxLength} Zeichen lang sein.")
            .OverridePropertyName(nameof(AddressData.Street))
            .When(a => !string.IsNullOrWhiteSpace(a.Street));
        RuleFor(a => a.Street2!.Trim())
            .MaximumLength(Address.StreetMaxLength)
            .WithMessage($"Der Adresszusatz darf höchstens {Address.StreetMaxLength} Zeichen lang sein.")
            .OverridePropertyName(nameof(AddressData.Street2))
            .When(a => !string.IsNullOrWhiteSpace(a.Street2));
        RuleFor(a => a.PostalCode!.Trim())
            .MaximumLength(Address.PostalCodeMaxLength)
            .WithMessage($"Die PLZ darf höchstens {Address.PostalCodeMaxLength} Zeichen lang sein.")
            .OverridePropertyName(nameof(AddressData.PostalCode))
            .When(a => !string.IsNullOrWhiteSpace(a.PostalCode));
        RuleFor(a => a.City!.Trim())
            .MaximumLength(Address.CityMaxLength)
            .WithMessage($"Der Ort darf höchstens {Address.CityMaxLength} Zeichen lang sein.")
            .OverridePropertyName(nameof(AddressData.City))
            .When(a => !string.IsNullOrWhiteSpace(a.City));
        RuleFor(a => a.Region!.Trim())
            .MaximumLength(Address.RegionMaxLength)
            .WithMessage($"Die Region darf höchstens {Address.RegionMaxLength} Zeichen lang sein.")
            .OverridePropertyName(nameof(AddressData.Region))
            .When(a => !string.IsNullOrWhiteSpace(a.Region));
        RuleFor(a => a.CountryCode)
            .Must(code => Countries.IsValidCode(code!.Trim()))
            .WithMessage("Bitte ein Land aus der Liste wählen (ISO-Code wie DE, AT, CH).")
            .When(a => !string.IsNullOrWhiteSpace(a.CountryCode));
    }
}
