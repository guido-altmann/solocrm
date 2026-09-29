using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Domain.Auditing;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.IntegrationTests.Features.Contacts;

public sealed class ContactHandlerTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    [Fact]
    public async Task Create_WithNewOrganizationName_CreatesOrganizationOfTypeOtherInSameSave()
    {
        var result = await SendAsync<CreateContact.Command, CreateContact.Result>(
            new CreateContact.Command("Ada", "Lovelace", NewOrganizationName: " Analytical Engines ", Source: LeadSource.Referral));

        result.IsSuccess.Should().BeTrue();
        await using var db = OpenDb();
        var organization = await db.Organizations.SingleAsync(Ct);
        organization.Name.Should().Be("Analytical Engines");
        organization.Type.Should().Be(OrganizationType.Other);
        var contact = await db.Contacts.SingleAsync(Ct);
        contact.OrganizationId.Should().Be(organization.Id);
        contact.Source.Should().Be(LeadSource.Referral);
        (await db.AuditEntries.Select(a => a.EntityType).ToListAsync(Ct)).Should().BeEquivalentTo("Contact", "Organization");
    }

    [Fact]
    public async Task Create_UnknownOrganization_ReturnsOrganizationNotFound()
    {
        var result = await SendAsync<CreateContact.Command, CreateContact.Result>(
            new CreateContact.Command("Ada", null, OrganizationId: Guid.CreateVersion7()));

        result.Error.Should().Be(ContactErrors.OrganizationNotFound);
        await using var db = OpenDb();
        (await db.Contacts.AnyAsync(Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Update_AssignsExistingOrganizationAndWritesAudit()
    {
        var organizationId = await CreateOrganizationAsync("Contoso");
        var id = await CreateContactAsync("Ada", "Lovelace", "ada@example.test");

        var result = await SendAsync<UpdateContact.Command, UpdateContact.Result>(new UpdateContact.Command(
            id, "Ada", "King", "ada@example.test", "+49 30 1234", "CTO", "https://linkedin.com/in/ada", organizationId, null, LeadSource.LinkedIn));

        result.Value.Should().Be(new UpdateContact.Result(id, organizationId));
        var loaded = await QueryAsync<GetContact.Query, GetContact.Result>(new GetContact.Query(id));
        loaded.Value.Should().Be(new GetContact.Result(
            id, "Ada", "King", "ada@example.test", "+49 30 1234", "CTO", "https://linkedin.com/in/ada",
            organizationId, "Contoso", OrganizationType.Other, LeadSource.LinkedIn, false));
        await using var db = OpenDb();
        var audit = await db.AuditEntries.SingleAsync(a => a.Action == AuditAction.Updated, Ct);
        audit.Changes.Should().Contain(
        [
            new AuditChange("LastName", "Lovelace", "King"),
            new AuditChange("OrganizationId", null, organizationId.ToString()),
        ]);
    }

    [Fact]
    public async Task Update_OwnEmailInDifferentCase_Succeeds()
    {
        var id = await CreateContactAsync("Ada", null, "ada@example.test");

        var result = await SendAsync<UpdateContact.Command, UpdateContact.Result>(
            new UpdateContact.Command(id, "Ada", null, "ADA@example.test", null, null, null, null, null, null));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Update_EmailOfOtherContact_ReturnsDuplicateEmail()
    {
        await CreateContactAsync("Grace", null, "grace@example.test");
        var id = await CreateContactAsync("Ada", null, "ada@example.test");

        var result = await SendAsync<UpdateContact.Command, UpdateContact.Result>(
            new UpdateContact.Command(id, "Ada", null, "Grace@example.test", null, null, null, null, null, null));

        result.Error.Should().Be(ContactErrors.DuplicateEmail);
    }

    [Fact]
    public async Task Update_UnknownId_ReturnsNotFound()
    {
        var result = await SendAsync<UpdateContact.Command, UpdateContact.Result>(
            new UpdateContact.Command(Guid.CreateVersion7(), "Ada", null, null, null, null, null, null, null, null));

        result.Error.Should().Be(ContactErrors.NotFound);
    }

    [Fact]
    public async Task Update_MissingName_ReturnsValidationError()
    {
        var id = await CreateContactAsync("Ada", null);

        var result = await SendAsync<UpdateContact.Command, UpdateContact.Result>(
            new UpdateContact.Command(id, " ", null, null, null, null, null, null, null, null));

        result.Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public async Task Archive_Contact_HidesItFromListUntilRestored()
    {
        var id = await CreateContactAsync("Ada", null);
        await CreateContactAsync("Grace", null);

        (await SendAsync<ArchiveContact.Command, ArchiveContact.Result>(new ArchiveContact.Command(id))).IsSuccess.Should().BeTrue();

        (await ListAsync(new GetContacts.Query())).Select(c => c.FirstName).Should().Equal("Grace");
        (await ListAsync(new GetContacts.Query(Search: "ada"))).Should().BeEmpty();
        (await ListAsync(new GetContacts.Query(IncludeArchived: true))).Should().HaveCount(2);

        (await SendAsync<RestoreContact.Command, RestoreContact.Result>(new RestoreContact.Command(id))).IsSuccess.Should().BeTrue();
        (await ListAsync(new GetContacts.Query())).Should().HaveCount(2);

        await using var db = OpenDb();
        (await db.AuditEntries.Where(a => a.EntityId == id).OrderBy(a => a.Id).Select(a => a.Action).ToListAsync(Ct))
            .Should().Equal(AuditAction.Created, AuditAction.Archived, AuditAction.Updated);
    }

    [Fact]
    public async Task GetContact_UnknownId_ReturnsNotFound()
    {
        (await QueryAsync<GetContact.Query, GetContact.Result>(new GetContact.Query(Guid.CreateVersion7())))
            .Error.Should().Be(ContactErrors.NotFound);
    }

    [Fact]
    public async Task GetContacts_InvalidFilter_ReturnsValidationError()
    {
        var result = await QueryAsync<GetContacts.Query, GetContacts.Result>(
            new GetContacts.Query(Source: (LeadSource)99, SortBy: (GetContacts.SortField)99));

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Keys.Should().BeEquivalentTo("Source", "SortBy");
    }

    [Fact]
    public async Task Archive_UnknownId_ReturnsNotFound()
    {
        (await SendAsync<ArchiveContact.Command, ArchiveContact.Result>(new ArchiveContact.Command(Guid.CreateVersion7())))
            .Error.Should().Be(ContactErrors.NotFound);
        (await SendAsync<RestoreContact.Command, RestoreContact.Result>(new RestoreContact.Command(Guid.CreateVersion7())))
            .Error.Should().Be(ContactErrors.NotFound);
    }

    [Fact]
    public async Task GetContacts_FiltersAndSorting_ReturnMatchingContacts()
    {
        var contoso = await CreateOrganizationAsync("Contoso");
        var fabrikam = await CreateOrganizationAsync("Fabrikam");
        await SendAsync<CreateContact.Command, CreateContact.Result>(new CreateContact.Command("Ada", "Lovelace", OrganizationId: fabrikam, Source: LeadSource.LinkedIn));
        await SendAsync<CreateContact.Command, CreateContact.Result>(new CreateContact.Command("Grace", "Hopper", OrganizationId: contoso, Source: LeadSource.Event));
        await SendAsync<CreateContact.Command, CreateContact.Result>(new CreateContact.Command("Alan", "Turing", Source: LeadSource.LinkedIn));

        (await ListAsync(new GetContacts.Query(OrganizationId: contoso))).Select(c => c.LastName).Should().Equal("Hopper");
        (await ListAsync(new GetContacts.Query(Source: LeadSource.LinkedIn))).Select(c => c.LastName).Should().Equal("Lovelace", "Turing");
        (await ListAsync(new GetContacts.Query(Search: "fabri"))).Select(c => c.OrganizationName).Should().Equal("Fabrikam");
        (await ListAsync(new GetContacts.Query(SortBy: GetContacts.SortField.Organization))).Select(c => c.OrganizationName)
            .Should().Equal("Contoso", "Fabrikam", null);
        (await ListAsync(new GetContacts.Query(SortDescending: true))).Select(c => c.LastName)
            .Should().Equal("Turing", "Lovelace", "Hopper");
    }

    [Fact]
    public async Task DeleteOrganization_AssignedContact_SetsOrganizationIdToNull()
    {
        var organizationId = await CreateOrganizationAsync("Contoso");
        var id = (await SendAsync<CreateContact.Command, CreateContact.Result>(
            new CreateContact.Command("Ada", null, OrganizationId: organizationId))).Value.Id;

        await using (var db = OpenDb())
        {
            await db.Database.ExecuteSqlAsync($"DELETE FROM organizations WHERE id = {organizationId}", Ct);
        }

        await using var verify = OpenDb();
        (await verify.Contacts.SingleAsync(c => c.Id == id, Ct)).OrganizationId.Should().BeNull();
    }

    private async Task<Guid> CreateContactAsync(string? firstName, string? lastName, string? email = null) =>
        (await SendAsync<CreateContact.Command, CreateContact.Result>(new CreateContact.Command(firstName, lastName, email))).Value.Id;

    private async Task<Guid> CreateOrganizationAsync(string name) =>
        (await SendAsync<CreateOrganization.Command, CreateOrganization.Result>(new CreateOrganization.Command(name))).Value.Id;

    private async Task<IReadOnlyList<GetContacts.Item>> ListAsync(GetContacts.Query query) =>
        (await QueryAsync<GetContacts.Query, GetContacts.Result>(query)).Value.Items;
}
