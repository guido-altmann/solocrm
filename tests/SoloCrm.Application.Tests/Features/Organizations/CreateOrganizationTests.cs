using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Application.Tests.Features.Organizations;

public sealed class CreateOrganizationTests
{
    private readonly DbSet<Organization> _organizations = Substitute.For<DbSet<Organization>>();
    private readonly ICrmDbContext _db = Substitute.For<ICrmDbContext>();
    private readonly ICrmDbContextFactory _dbFactory = Substitute.For<ICrmDbContextFactory>();
    private readonly CreateOrganization.Handler _handler;

    public CreateOrganizationTests()
    {
        _db.Organizations.Returns(_organizations);
        _dbFactory.CreateDbContextAsync(Arg.Any<CancellationToken>()).Returns(_db);
        _handler = new CreateOrganization.Handler(_dbFactory, new CreateOrganization.Validator());
    }

    [Fact]
    public async Task Handle_ValidCommand_AddsOrganizationAndSaves()
    {
        var ct = TestContext.Current.CancellationToken;
        Organization? added = null;
        _organizations.Add(Arg.Do<Organization>(o => added = o));

        var result = await _handler.Handle(new CreateOrganization.Command("Contoso", OrganizationType.Agency, "contoso.de"), ct);

        result.IsSuccess.Should().BeTrue();
        added!.Id.Should().Be(result.Value.Id);
        added.Type.Should().Be(OrganizationType.Agency);
        added.Website.Should().Be("https://contoso.de");
        await _db.Received(1).SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Handle_InvalidCommand_ReturnsFieldErrorsWithoutTouchingDatabase()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await _handler.Handle(
            new CreateOrganization.Command(" ", (OrganizationType)42, "no url", new string('x', Organization.CityMaxLength + 1)),
            ct);

        result.Error.Should().BeOfType<ValidationError>()
            .Which.Errors.Keys.Should().BeEquivalentTo("Name", "Type", "Website", "City");
        await _dbFactory.DidNotReceiveWithAnyArgs().CreateDbContextAsync(ct);
    }

    [Fact]
    public void Validator_WebsiteTooLongAfterNormalization_Fails()
    {
        var website = "example.com/" + new string('a', Organization.WebsiteMaxLength - 12);

        var validation = new CreateOrganization.Validator().Validate(new CreateOrganization.Command("Contoso", Website: website));

        validation.Errors.Should().ContainSingle(e => e.PropertyName == "Website");
    }
}
