using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Application.Features.Opportunities;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Application.Features.Tags;
using SoloCrm.Application.Features.Tasks;
using SoloCrm.Application.Features.Timeline;

namespace SoloCrm.IntegrationTests.Features.Api;

/// <summary>Read use cases added for the REST API (SPEC 5): request list, single task, organization by name.</summary>
public sealed class ApiQueryHandlerTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    [Fact]
    public async Task GetOpportunities_SearchStageTagAndArchived_FiltersAndPages()
    {
        var migration = await CreateOpportunityAsync("Migration Azure");
        Time.Advance(TimeSpan.FromMinutes(1));
        var newest = await CreateOpportunityAsync("Workshop Blazor");
        Time.Advance(TimeSpan.FromMinutes(1));
        var archived = await CreateOpportunityAsync("Migration AWS");
        await SendAsync<ArchiveOpportunity.Command, ArchiveOpportunity.Result>(new ArchiveOpportunity.Command(archived));
        var tag = await SendAsync<AssignTag.Command, AssignTag.Result>(
            new AssignTag.Command(TimelineRecordType.Opportunity, migration, null, "Cloud"));

        var all = await ListAsync(new GetOpportunities.Query());
        var search = await ListAsync(new GetOpportunities.Query("Migraton"));
        var withArchived = await ListAsync(new GetOpportunities.Query("Migration", IncludeArchived: true));
        var tagged = await ListAsync(new GetOpportunities.Query(TagIds: [tag.Value.TagId]));
        var page = await ListAsync(new GetOpportunities.Query(PageIndex: 1, PageSize: 1));

        all.Items.Select(i => i.Id).Should().Equal(newest, migration);
        all.Items.Should().AllSatisfy(i => i.StageName.Should().Be("Neu"));
        search.Items.Select(i => i.Id).Should().Equal(migration);
        withArchived.Items.Select(i => i.Id).Should().BeEquivalentTo([migration, archived]);
        tagged.Items.Single().Tags.Single().Name.Should().Be("Cloud");
        page.Items.Select(i => i.Id).Should().Equal(migration);
        page.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetOpportunities_InvalidPaging_ReturnsValidationError()
    {
        var result = await QueryAsync<GetOpportunities.Query, GetOpportunities.Result>(new GetOpportunities.Query(PageSize: 0));

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("PageSize");
    }

    [Fact]
    public async Task GetTask_ExistingAndUnknown_ReturnsTaskOrNotFound()
    {
        var created = await SendAsync<CreateTask.Command, CreateTask.Result>(new CreateTask.Command("Nachfassen", new DateOnly(2026, 10, 5)));

        var task = await QueryAsync<GetTask.Query, GetTask.Result>(new GetTask.Query(created.Value.Id));
        var unknown = await QueryAsync<GetTask.Query, GetTask.Result>(new GetTask.Query(Guid.NewGuid()));

        task.Value.Title.Should().Be("Nachfassen");
        task.Value.DueDate.Should().Be(new DateOnly(2026, 10, 5));
        task.Value.CompletedAt.Should().BeNull();
        unknown.Error.Should().Be(TaskErrors.NotFound);
    }

    [Fact]
    public async Task FindOrganizationByName_ExactNameIgnoringCase_ReturnsActiveOrganization()
    {
        var contoso = await SendAsync<CreateOrganization.Command, CreateOrganization.Result>(new CreateOrganization.Command("Contoso AG"));
        await SendAsync<CreateOrganization.Command, CreateOrganization.Result>(new CreateOrganization.Command("Contoso_AG Holding"));
        var archived = await SendAsync<CreateOrganization.Command, CreateOrganization.Result>(new CreateOrganization.Command("contoso ag"));
        await SendAsync<ArchiveOrganization.Command, ArchiveOrganization.Result>(new ArchiveOrganization.Command(archived.Value.Id));

        var found = await FindAsync(" CONTOSO ag ");
        var wildcard = await FindAsync("Contoso%");
        var underscore = await FindAsync("Contoso_AG");

        found.Value.Id.Should().Be(contoso.Value.Id);
        wildcard.Value.Id.Should().BeNull();
        underscore.Value.Id.Should().BeNull("_ is not a wildcard");
    }

    [Fact]
    public async Task FindOrganizationByName_EmptyName_ReturnsValidationError()
    {
        var result = await FindAsync(" ");

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Name");
    }

    private async Task<Guid> CreateOpportunityAsync(string title) =>
        (await SendAsync<CreateOpportunity.Command, CreateOpportunity.Result>(new CreateOpportunity.Command(title))).Value.Id;

    private async Task<GetOpportunities.Result> ListAsync(GetOpportunities.Query query) =>
        (await QueryAsync<GetOpportunities.Query, GetOpportunities.Result>(query)).Value;

    private Task<Result<FindOrganizationByName.Result>> FindAsync(string name) =>
        QueryAsync<FindOrganizationByName.Query, FindOrganizationByName.Result>(new FindOrganizationByName.Query(name));
}
