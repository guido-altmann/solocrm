using SoloCrm.Application.Features.Contacts;
using SoloCrm.Domain.Common;

namespace SoloCrm.Application.Tests.Features.Contacts;

public sealed class UpdateContactValidatorTests
{
    private readonly UpdateContact.Validator _validator = new();

    [Fact]
    public void Validate_ExistingAndNewOrganization_FailsAtNewOrganizationName()
    {
        var result = _validator.Validate(Command() with { OrganizationId = Guid.CreateVersion7(), NewOrganizationName = "Neu GmbH" });

        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be(nameof(UpdateContact.Command.NewOrganizationName));
    }

    [Fact]
    public void Validate_UnknownSource_Fails()
    {
        var result = _validator.Validate(Command() with { Source = (LeadSource)99 });

        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be(nameof(UpdateContact.Command.Source));
    }

    [Fact]
    public void Validate_NewOrganizationNameTooLong_Fails()
    {
        var result = _validator.Validate(Command() with { NewOrganizationName = new string('x', 201) });

        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be(nameof(UpdateContact.Command.NewOrganizationName));
    }

    [Fact]
    public void Validate_CompleteCommand_Succeeds()
    {
        _validator.Validate(Command() with { NewOrganizationName = "Neu GmbH", Source = LeadSource.Event }).IsValid.Should().BeTrue();
    }

    private static UpdateContact.Command Command() =>
        new(Guid.CreateVersion7(), "Ada", "Lovelace", "ada@example.test", null, null, "https://linkedin.com/in/ada", null, null, null);
}
