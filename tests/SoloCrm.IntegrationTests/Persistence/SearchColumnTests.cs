using SoloCrm.Application.Features.Common;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Application.Features.Search;
using SoloCrm.IntegrationTests.Features;

namespace SoloCrm.IntegrationTests.Persistence;

/// <summary>
/// Generated search columns (ADR-007): PostgreSQL computes them on insert and update, EF only reads them,
/// and the audit ignores them.
/// </summary>
public sealed class SearchColumnTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    [Fact]
    public async Task Create_Contact_GeneratesSearchVectorAndName()
    {
        var id = await CreateContactAsync("Ada", "Lovelace", "ada.king@example.test");

        await using var db = OpenDb();
        var row = await db.Contacts
            .Where(c => c.Id == id)
            .Select(c => new
            {
                Name = EF.Property<string>(c, SearchColumns.Name),
                Vector = EF.Property<NpgsqlTsVector>(c, SearchColumns.Vector),
            })
            .SingleAsync(Ct);

        row.Name.Should().Be("Ada Lovelace");
        row.Vector.Select(l => l.Text).Should().BeEquivalentTo("ada", "lovelace", "king", "example", "test");
    }

    [Fact]
    public async Task Update_Contact_RecomputesSearchColumnsWithoutAuditingThem()
    {
        var id = await CreateContactAsync("Ada", "Lovelace");

        await SendAsync<UpdateContact.Command, UpdateContact.Result>(
            new UpdateContact.Command(id, null, "King", null, null, null, null, null, null, null, null));

        await using var db = OpenDb();
        (await db.Contacts.Where(c => c.Id == id).Select(c => EF.Property<string>(c, SearchColumns.Name)).SingleAsync(Ct))
            .Should().Be("King");
        var fields = (await db.AuditEntries.ToListAsync(Ct)).SelectMany(a => a.Changes).Select(c => c.Field);
        fields.Should().NotContain([SearchColumns.Name, SearchColumns.Vector]);
    }

    [Fact]
    public async Task Create_Organization_SplitsWebsiteIntoWords()
    {
        var id = (await SendAsync<CreateOrganization.Command, CreateOrganization.Result>(
            new CreateOrganization.Command("Contoso GmbH", Website: "https://www.contoso-labs.de/team", Address: new AddressData(City: "Köln")))).Value.Id;

        await using var db = OpenDb();
        var matches = await db.Organizations
            .Where(o => EF.Property<NpgsqlTsVector>(o, SearchColumns.Vector).Matches(EF.Functions.ToTsQuery("simple", "labs:* & köln")))
            .Select(o => o.Id)
            .ToListAsync(Ct);

        matches.Should().Equal(id);
    }

    [Fact]
    public async Task Migrate_CreatesTrigramExtensionAndSearchIndexes()
    {
        await using var db = OpenDb();

        var extensions = await db.Database
            .SqlQueryRaw<string>("SELECT extname AS \"Value\" FROM pg_extension")
            .ToListAsync(Ct);
        var indexes = await db.Database
            .SqlQueryRaw<string>("SELECT indexname AS \"Value\" FROM pg_indexes WHERE indexname LIKE '%search%' OR indexname LIKE '%trgm'")
            .ToListAsync(Ct);

        extensions.Should().Contain("pg_trgm");
        indexes.Should().BeEquivalentTo(
            "ix_contacts_search_vector",
            "ix_contacts_search_name_trgm",
            "ix_organizations_search_vector",
            "ix_organizations_name_trgm",
            "ix_opportunities_search_vector",
            "ix_opportunities_title_trgm");
    }

    private async Task<Guid> CreateContactAsync(string? firstName, string? lastName, string? email = null) =>
        (await SendAsync<CreateContact.Command, CreateContact.Result>(new CreateContact.Command(firstName, lastName, email))).Value.Id;
}
