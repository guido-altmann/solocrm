using SoloCrm.Application.Features.Common;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Domain.Auditing;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.IntegrationTests.Features.Organizations;

public sealed class OrganizationHandlerTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    [Fact]
    public async Task Create_ValidCommand_PersistsNormalizedOrganizationWithAuditAndEvent()
    {
        var result = await CreateAsync(" Contoso GmbH ", OrganizationType.Client, "Contoso.DE/", " Berlin ");

        await using var db = OpenDb();
        var stored = await db.Organizations.SingleAsync(o => o.Id == result, Ct);
        stored.Name.Should().Be("Contoso GmbH");
        stored.Type.Should().Be(OrganizationType.Client);
        stored.Website.Should().Be("https://contoso.de");
        stored.Address.City.Should().Be("Berlin");
        stored.CreatedAt.Should().Be(Start);
        (await db.AuditEntries.SingleAsync(Ct)).Action.Should().Be(AuditAction.Created);
        (await db.OutboxMessages.SingleAsync(Ct)).Type.Should().Be("organization.created");
    }

    [Fact]
    public async Task Create_InvalidWebsite_ReturnsValidationError()
    {
        var result = await SendAsync<CreateOrganization.Command, CreateOrganization.Result>(
            new CreateOrganization.Command("Contoso", Website: "not a url"));

        result.Error.Should().BeOfType<ValidationError>()
            .Which.Errors.Should().ContainKey(nameof(CreateOrganization.Command.Website));
    }

    [Fact]
    public async Task Update_ExistingOrganization_ChangesFieldsAndWritesUpdatedAudit()
    {
        var id = await CreateAsync("Contoso", OrganizationType.Other);

        var result = await SendAsync<UpdateOrganization.Command, UpdateOrganization.Result>(
            new UpdateOrganization.Command(id, "Contoso AG", OrganizationType.Agency, null, new AddressData(City: "Köln"), "Stammkunde"));

        result.IsSuccess.Should().BeTrue();
        var loaded = await QueryAsync<GetOrganization.Query, GetOrganization.Result>(new GetOrganization.Query(id));
        loaded.Value.Should().Be(new GetOrganization.Result(id, "Contoso AG", OrganizationType.Agency, null, new AddressData(City: "Köln"), "Stammkunde", false));
        await using var db = OpenDb();
        var audit = await db.AuditEntries.SingleAsync(a => a.Action == AuditAction.Updated, Ct);
        audit.Changes.Should().Contain(new AuditChange("Type", "Other", "Agency"));
    }

    [Fact]
    public async Task Update_UnknownId_ReturnsNotFound()
    {
        var result = await SendAsync<UpdateOrganization.Command, UpdateOrganization.Result>(
            new UpdateOrganization.Command(Guid.CreateVersion7(), "Contoso", OrganizationType.Other, null, null, null));

        result.Error.Should().Be(OrganizationErrors.NotFound);
    }

    [Fact]
    public async Task Update_MissingName_ReturnsValidationError()
    {
        var id = await CreateAsync("Contoso");

        var result = await SendAsync<UpdateOrganization.Command, UpdateOrganization.Result>(
            new UpdateOrganization.Command(id, " ", OrganizationType.Other, null, null, null));

        result.Error.Should().BeOfType<ValidationError>()
            .Which.Errors.Should().ContainKey(nameof(UpdateOrganization.Command.Name));
    }

    [Fact]
    public async Task Get_UnknownId_ReturnsNotFound()
    {
        var result = await QueryAsync<GetOrganization.Query, GetOrganization.Result>(new GetOrganization.Query(Guid.CreateVersion7()));

        result.Error.Should().Be(OrganizationErrors.NotFound);
    }

    [Fact]
    public async Task Archive_ThenRestore_TogglesVisibilityAndWritesAudit()
    {
        var id = await CreateAsync("Contoso");

        (await SendAsync<ArchiveOrganization.Command, ArchiveOrganization.Result>(new ArchiveOrganization.Command(id)))
            .IsSuccess.Should().BeTrue();
        (await ListAsync(new GetOrganizations.Query())).Should().BeEmpty();
        (await ListAsync(new GetOrganizations.Query(IncludeArchived: true))).Should().ContainSingle().Which.IsArchived.Should().BeTrue();

        (await SendAsync<RestoreOrganization.Command, RestoreOrganization.Result>(new RestoreOrganization.Command(id)))
            .IsSuccess.Should().BeTrue();
        (await ListAsync(new GetOrganizations.Query())).Should().ContainSingle();

        await using var db = OpenDb();
        (await db.AuditEntries.OrderBy(a => a.Id).Select(a => a.Action).ToListAsync(Ct))
            .Should().Equal(AuditAction.Created, AuditAction.Archived, AuditAction.Updated);
    }

    [Fact]
    public async Task Archive_UnknownId_ReturnsNotFound()
    {
        var result = await SendAsync<ArchiveOrganization.Command, ArchiveOrganization.Result>(
            new ArchiveOrganization.Command(Guid.CreateVersion7()));

        result.Error.Should().Be(OrganizationErrors.NotFound);
    }

    [Fact]
    public async Task GetOrganizations_FilterSearchAndSort_ReturnsMatchingPage()
    {
        await CreateAsync("Beta Consulting", OrganizationType.Agency, city: "Hamburg");
        await CreateAsync("Alpha Recruiting", OrganizationType.Agency, city: "München");
        await CreateAsync("Gamma AG", OrganizationType.Client, city: "Hamburg");

        (await ListAsync(new GetOrganizations.Query(Type: OrganizationType.Agency))).Select(o => o.Name)
            .Should().Equal("Alpha Recruiting", "Beta Consulting");
        (await ListAsync(new GetOrganizations.Query(Search: "hamb", SortBy: GetOrganizations.SortField.Name, SortDescending: true)))
            .Select(o => o.Name)
            .Should().Equal("Gamma AG", "Beta Consulting");
        (await ListAsync(new GetOrganizations.Query(SortBy: GetOrganizations.SortField.City))).Select(o => o.City)
            .Should().Equal("Hamburg", "Hamburg", "München");

        var page = await QueryAsync<GetOrganizations.Query, GetOrganizations.Result>(new GetOrganizations.Query(PageIndex: 1, PageSize: 2));
        page.Value.TotalCount.Should().Be(3);
        page.Value.Items.Select(o => o.Name).Should().Equal("Gamma AG");
    }

    [Fact]
    public async Task GetOrganizations_SearchWithQuerySyntax_TreatsInputAsWords()
    {
        await CreateAsync("Rock & Roll GmbH");
        await CreateAsync("Rolling Stones Ltd");

        (await ListAsync(new GetOrganizations.Query(Search: "rock & !(roll:*"))).Select(o => o.Name)
            .Should().Equal("Rock & Roll GmbH");
        (await ListAsync(new GetOrganizations.Query(Search: "%_&|"))).Should().HaveCount(2, "input without words does not filter");
    }

    [Fact]
    public async Task GetOrganizations_InvalidPageSize_ReturnsValidationError()
    {
        var result = await QueryAsync<GetOrganizations.Query, GetOrganizations.Result>(new GetOrganizations.Query(PageSize: 0));

        result.Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public async Task Search_PreferredType_ListsMatchingTypeFirstAndExcludesArchived()
    {
        await CreateAsync("Contoso Staffing", OrganizationType.Agency);
        await CreateAsync("Contoso Bank", OrganizationType.Client);
        await CreateAsync("Big Contoso", OrganizationType.Agency);
        var archived = await CreateAsync("Contoso Old", OrganizationType.Agency);
        await SendAsync<ArchiveOrganization.Command, ArchiveOrganization.Result>(new ArchiveOrganization.Command(archived));

        var result = await QueryAsync<SearchOrganizations.Query, SearchOrganizations.Result>(
            new SearchOrganizations.Query("contoso", OrganizationType.Agency));

        var names = result.Value.Items.Select(o => o.Name).ToList();
        names.Take(2).Should().BeEquivalentTo("Contoso Staffing", "Big Contoso");
        names.Last().Should().Be("Contoso Bank");
    }

    [Fact]
    public async Task Search_InvalidLimit_ReturnsValidationError()
    {
        var result = await QueryAsync<SearchOrganizations.Query, SearchOrganizations.Result>(new SearchOrganizations.Query("x", Limit: 0));

        result.Error.Should().BeOfType<ValidationError>();
    }

    private async Task<Guid> CreateAsync(
        string name,
        OrganizationType type = OrganizationType.Other,
        string? website = null,
        string? city = null)
    {
        var result = await SendAsync<CreateOrganization.Command, CreateOrganization.Result>(
            new CreateOrganization.Command(name, type, website, new AddressData(City: city)));
        result.IsSuccess.Should().BeTrue();
        return result.Value.Id;
    }

    private async Task<IReadOnlyList<GetOrganizations.Item>> ListAsync(GetOrganizations.Query query) =>
        (await QueryAsync<GetOrganizations.Query, GetOrganizations.Result>(query)).Value.Items;
}
