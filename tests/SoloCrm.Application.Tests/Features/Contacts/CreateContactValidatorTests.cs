using SoloCrm.Application.Features.Contacts;
using SoloCrm.Domain.Contacts;

namespace SoloCrm.Application.Tests.Features.Contacts;

public sealed class CreateContactValidatorTests
{
    private readonly CreateContact.Validator _validator = new();

    [Theory]
    [InlineData("Ada", null)]
    [InlineData(null, "Lovelace")]
    [InlineData("Ada", "Lovelace")]
    public void Validate_FirstOrLastName_IsValid(string? firstName, string? lastName)
    {
        var result = _validator.Validate(new CreateContact.Command(firstName, lastName));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("  ", "\t")]
    public void Validate_MissingName_FailsOnLastName(string? firstName, string? lastName)
    {
        var result = _validator.Validate(new CreateContact.Command(firstName, lastName));

        result.Errors.Should().ContainSingle()
            .Which.Should().Match<FluentValidation.Results.ValidationFailure>(f =>
                f.PropertyName == nameof(CreateContact.Command.LastName)
                && f.ErrorMessage == "Bitte Vor- oder Nachnamen angeben.");
    }

    [Fact]
    public void Validate_AllOptionalFieldsSet_IsValid()
    {
        var command = new CreateContact.Command(
            "Ada",
            "Lovelace",
            " ada@example.test ",
            "+49 30 1234567",
            "CTO",
            "https://www.linkedin.com/in/ada");

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("no-at-sign")]
    [InlineData("@example.test")]
    public void Validate_InvalidEmail_FailsOnEmail(string email)
    {
        var result = _validator.Validate(new CreateContact.Command("Ada", null, Email: email));

        result.Errors.Should().ContainSingle()
            .Which.PropertyName.Should().Be(nameof(CreateContact.Command.Email));
    }

    [Fact]
    public void Validate_BlankEmail_IsValid()
    {
        var result = _validator.Validate(new CreateContact.Command("Ada", null, Email: "   "));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("linkedin.com/in/ada")]
    [InlineData("ftp://linkedin.com/in/ada")]
    public void Validate_InvalidLinkedInUrl_FailsOnLinkedInUrl(string url)
    {
        var result = _validator.Validate(new CreateContact.Command("Ada", null, LinkedInUrl: url));

        result.Errors.Should().ContainSingle()
            .Which.PropertyName.Should().Be(nameof(CreateContact.Command.LinkedInUrl));
    }

    [Fact]
    public void Validate_TooLongValues_FailsPerField()
    {
        var command = new CreateContact.Command(
            new string('a', Contact.FirstNameMaxLength + 1),
            new string('b', Contact.LastNameMaxLength + 1),
            Phone: new string('1', Contact.PhoneMaxLength + 1),
            JobTitle: new string('c', Contact.JobTitleMaxLength + 1));

        var result = _validator.Validate(command);

        result.Errors.Select(f => f.PropertyName).Should().BeEquivalentTo(
            nameof(CreateContact.Command.FirstName),
            nameof(CreateContact.Command.LastName),
            nameof(CreateContact.Command.Phone),
            nameof(CreateContact.Command.JobTitle));
    }
}
