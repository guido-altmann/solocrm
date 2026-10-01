using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Common;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Domain.Auditing;
using SoloCrm.Domain.Common;

namespace SoloCrm.IntegrationTests.Features.Contacts;

/// <summary>Addresses of contacts and organizations (iteration 5 decision 14).</summary>
public sealed class AddressHandlerTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    private static readonly AddressData Berlin = new("Hauptstr. 1", "Aufgang B", "10115", "Berlin", "Berlin", "DE");

    [Fact]
    public async Task CreateAndUpdateContact_Address_IsStoredLoadedAndAudited()
    {
        var created = await SendAsync<CreateContact.Command, CreateContact.Result>(new CreateContact.Command("Ada", "Lovelace", Address: Berlin));
        var id = created.Value.Id;

        var loaded = await QueryAsync<GetContact.Query, GetContact.Result>(new GetContact.Query(id));
        loaded.Value.Address.Should().Be(Berlin);

        var moved = Berlin with { Street = "Seestr. 5", Street2 = null, CountryCode = "AT", City = "Wien", PostalCode = "1010", Region = null };
        await SendAsync<UpdateContact.Command, UpdateContact.Result>(new UpdateContact.Command(
            id, "Ada", "Lovelace", null, null, null, null, null, null, null, moved));

        (await QueryAsync<GetContact.Query, GetContact.Result>(new GetContact.Query(id))).Value.Address.Should().Be(moved);
        await using var db = OpenDb();
        var update = await db.AuditEntries.SingleAsync(a => a.EntityId == id && a.Action == AuditAction.Updated, Ct);
        update.Changes.Should().Contain(new AuditChange("Address.City", "Berlin", "Wien"));
        update.Changes.Should().Contain(new AuditChange("Address.CountryCode", "DE", "AT"));
    }

    [Fact]
    public async Task UpdateContact_NullAddress_ClearsIt()
    {
        var id = (await SendAsync<CreateContact.Command, CreateContact.Result>(new CreateContact.Command("Ada", "Lovelace", Address: Berlin))).Value.Id;

        await SendAsync<UpdateContact.Command, UpdateContact.Result>(new UpdateContact.Command(
            id, "Ada", "Lovelace", null, null, null, null, null, null, null, null));

        await using var db = OpenDb();
        (await db.Contacts.SingleAsync(Ct)).Address.Should().Be(Address.Empty);
    }

    [Fact]
    public async Task CreateContact_InvalidAddress_ReturnsFieldErrors()
    {
        var result = await SendAsync<CreateContact.Command, CreateContact.Result>(new CreateContact.Command(
            "Ada", "Lovelace", Address: new AddressData(PostalCode: new string('1', 21), CountryCode: "Deutschland")));

        result.Error.Should().BeOfType<ValidationError>()
            .Which.Errors.Keys.Should().BeEquivalentTo("Address.PostalCode", "Address.CountryCode");
    }

    [Fact]
    public async Task Organization_Address_KeepsCityColumnForSearchAndSorting()
    {
        var id = (await SendAsync<CreateOrganization.Command, CreateOrganization.Result>(
            new CreateOrganization.Command("Contoso", Address: Berlin with { City = "Freiburg im Breisgau" }))).Value.Id;

        var found = await QueryAsync<GetOrganizations.Query, GetOrganizations.Result>(new GetOrganizations.Query("Breisgau"));
        var loaded = await QueryAsync<GetOrganization.Query, GetOrganization.Result>(new GetOrganization.Query(id));

        found.Value.Items.Single().City.Should().Be("Freiburg im Breisgau");
        loaded.Value.Address.Should().Be(Berlin with { City = "Freiburg im Breisgau" });
        await using var db = OpenDb();
        (await db.Database.SqlQuery<string>($"SELECT city AS \"Value\" FROM organizations").SingleAsync(Ct)).Should().Be("Freiburg im Breisgau");
    }
}
