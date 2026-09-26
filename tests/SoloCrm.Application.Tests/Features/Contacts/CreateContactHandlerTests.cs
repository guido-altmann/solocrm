using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Domain.Contacts;

namespace SoloCrm.Application.Tests.Features.Contacts;

/// <summary>
/// Handler logic without a database. Queries (duplicate email) are covered by the integration tests.
/// </summary>
public sealed class CreateContactHandlerTests
{
    private readonly DbSet<Contact> _contacts = Substitute.For<DbSet<Contact>>();
    private readonly ICrmDbContext _db = Substitute.For<ICrmDbContext>();
    private readonly ICrmDbContextFactory _dbFactory = Substitute.For<ICrmDbContextFactory>();
    private readonly CreateContact.Handler _handler;

    public CreateContactHandlerTests()
    {
        _db.Contacts.Returns(_contacts);
        _dbFactory.CreateDbContextAsync(Arg.Any<CancellationToken>()).Returns(_db);
        _handler = new CreateContact.Handler(_dbFactory, new CreateContact.Validator());
    }

    [Fact]
    public async Task Handle_ValidCommandWithoutEmail_AddsContactAndSaves()
    {
        var ct = TestContext.Current.CancellationToken;
        Contact? added = null;
        _contacts.Add(Arg.Do<Contact>(c => added = c));

        var result = await _handler.Handle(new CreateContact.Command(" Ada ", "Lovelace", JobTitle: "CTO"), ct);

        result.IsSuccess.Should().BeTrue();
        added.Should().NotBeNull();
        added!.Id.Should().Be(result.Value.Id);
        added.FirstName.Should().Be("Ada");
        added.LastName.Should().Be("Lovelace");
        added.JobTitle.Should().Be("CTO");
        await _db.Received(1).SaveChangesAsync(ct);
        await _db.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task Handle_MissingName_ReturnsValidationErrorWithoutTouchingDatabase()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await _handler.Handle(new CreateContact.Command(null, " ", Email: "ada@example.test"), ct);

        result.IsFailure.Should().BeTrue();
        var error = result.Error.Should().BeOfType<ValidationError>().Subject;
        error.Errors.Should().ContainKey(nameof(CreateContact.Command.LastName))
            .WhoseValue.Should().Equal("Bitte Vor- oder Nachnamen angeben.");
        await _dbFactory.DidNotReceiveWithAnyArgs().CreateDbContextAsync(ct);
    }

    [Fact]
    public async Task Handle_InvalidEmail_ReturnsValidationErrorForEmail()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await _handler.Handle(new CreateContact.Command("Ada", null, Email: "no-at-sign"), ct);

        result.Error.Should().BeOfType<ValidationError>()
            .Which.Errors.Should().ContainKey(nameof(CreateContact.Command.Email));
        await _dbFactory.DidNotReceiveWithAnyArgs().CreateDbContextAsync(ct);
    }
}
