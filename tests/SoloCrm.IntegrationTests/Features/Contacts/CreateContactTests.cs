using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Domain.Auditing;
using SoloCrm.IntegrationTests.Web;

namespace SoloCrm.IntegrationTests.Features.Contacts;

/// <summary>
/// Runs the handler through the production DI setup against a migrated database.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CreateContactTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 8, 30, 0, TimeSpan.Zero);

    private string _connectionString = "";
    private ServiceProvider _services = null!;

    private ICommandHandler<CreateContact.Command, CreateContact.Result> Handler =>
        _services.GetRequiredService<ICommandHandler<CreateContact.Command, CreateContact.Result>>();

    public async ValueTask InitializeAsync()
    {
        _connectionString = await CrmWebApplicationFactory.CreateDatabaseAsync(postgres, TestContext.Current.CancellationToken);
        _services = CrmServices.Create(_connectionString, new FakeTimeProvider(Now));
    }

    public async ValueTask DisposeAsync() => await _services.DisposeAsync();

    [Fact]
    public async Task Migrate_FreshDatabase_MatchesCurrentModel()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CrmWebApplicationFactory.CreateDbContext(_connectionString);

        (await db.Database.GetPendingMigrationsAsync(ct)).Should().BeEmpty();
        db.Database.HasPendingModelChanges().Should().BeFalse("every model change needs a migration");
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsContactWithTimestamps()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await Handler.Handle(
            new CreateContact.Command("Ada", "Lovelace", " Ada@Example.test ", JobTitle: "CTO"),
            ct);

        result.IsSuccess.Should().BeTrue();
        await using var db = CrmWebApplicationFactory.CreateDbContext(_connectionString);
        var stored = await db.Contacts.SingleAsync(c => c.Id == result.Value.Id, ct);
        stored.FirstName.Should().Be("Ada");
        stored.LastName.Should().Be("Lovelace");
        stored.Email.Should().Be("Ada@Example.test");
        stored.JobTitle.Should().Be("CTO");
        stored.CreatedAt.Should().Be(Now);
        stored.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public async Task Handle_ValidCommand_WritesCreatedAuditEntry()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await Handler.Handle(new CreateContact.Command("Ada", "Lovelace", "ada@example.test"), ct);

        await using var db = CrmWebApplicationFactory.CreateDbContext(_connectionString);
        var entry = await db.AuditEntries.SingleAsync(ct);
        entry.EntityType.Should().Be("Contact");
        entry.EntityId.Should().Be(result.Value.Id);
        entry.Action.Should().Be(AuditAction.Created);
        entry.OccurredAt.Should().Be(Now);
        entry.Changes.Should().BeEquivalentTo(
        [
            new AuditChange("Email", null, "ada@example.test"),
            new AuditChange("FirstName", null, "Ada"),
            new AuditChange("LastName", null, "Lovelace"),
        ]);
    }

    [Fact]
    public async Task Handle_ValidCommand_WritesContactCreatedToOutbox()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await Handler.Handle(new CreateContact.Command("Ada", null), ct);

        await using var db = CrmWebApplicationFactory.CreateDbContext(_connectionString);
        var message = await db.OutboxMessages.SingleAsync(ct);
        message.Type.Should().Be("contact.created");
        message.Payload.Should().Be($$"""{"contactId": "{{result.Value.Id}}"}""");
        message.OccurredAt.Should().Be(Now);
        message.NextAttemptAt.Should().Be(Now);
        message.ProcessedAt.Should().BeNull();
        message.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task Handle_EmailExistsWithDifferentCase_ReturnsDuplicateEmail()
    {
        var ct = TestContext.Current.CancellationToken;
        (await Handler.Handle(new CreateContact.Command("Ada", null, "ada@example.test"), ct))
            .IsSuccess.Should().BeTrue();

        var result = await Handler.Handle(new CreateContact.Command("Other", null, "ADA@example.TEST"), ct);

        result.Error.Should().Be(CreateContact.Errors.DuplicateEmail);
        await using var db = CrmWebApplicationFactory.CreateDbContext(_connectionString);
        (await db.Contacts.CountAsync(ct)).Should().Be(1);
    }

    [Fact]
    public async Task Handle_MissingName_ReturnsValidationErrorAndPersistsNothing()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await Handler.Handle(new CreateContact.Command(null, null, "ada@example.test"), ct);

        result.Error.Should().BeOfType<ValidationError>();
        await using var db = CrmWebApplicationFactory.CreateDbContext(_connectionString);
        (await db.Contacts.AnyAsync(ct)).Should().BeFalse();
    }
}
