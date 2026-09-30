using SoloCrm.Application.Features.Contacts;
using SoloCrm.Application.Features.Opportunities;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Application.Features.Pipeline;
using SoloCrm.Application.Features.Tags;
using SoloCrm.Application.Features.Timeline;

namespace SoloCrm.IntegrationTests.Features.Tags;

/// <summary>Tags in the lists and on pipeline cards, and the tag filter (US-15, US-04 AK2).</summary>
public sealed class TagListTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    [Fact]
    public async Task GetContacts_SeveralTags_FiltersOrCombinedAndListsTagsByName()
    {
        var both = await CreateContactAsync("Ada");
        var remoteOnly = await CreateContactAsync("Grace");
        await CreateContactAsync("Linus");
        var remote = await AssignAsync(TimelineRecordType.Contact, both, "Remote");
        var azure = await AssignAsync(TimelineRecordType.Contact, both, "Azure");
        await AssignAsync(TimelineRecordType.Contact, remoteOnly, "Remote");

        var result = await QueryAsync<GetContacts.Query, GetContacts.Result>(new GetContacts.Query(TagIds: [remote, azure]));

        result.Value.TotalCount.Should().Be(2);
        result.Value.Items.Select(c => c.Id).Should().BeEquivalentTo([both, remoteOnly]);
        result.Value.Items.Single(c => c.Id == both).Tags.Select(t => t.Name).Should().Equal("Azure", "Remote");
    }

    [Fact]
    public async Task GetContacts_NoTagFilter_ListsAllWithEmptyTags()
    {
        var id = await CreateContactAsync("Ada");

        var result = await QueryAsync<GetContacts.Query, GetContacts.Result>(new GetContacts.Query(TagIds: []));

        result.Value.Items.Should().ContainSingle(c => c.Id == id).Which.Tags.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOrganizations_TagFilter_ReturnsTaggedOrganizationsWithTags()
    {
        var tagged = (await SendAsync<CreateOrganization.Command, CreateOrganization.Result>(new CreateOrganization.Command("Contoso"))).Value.Id;
        await SendAsync<CreateOrganization.Command, CreateOrganization.Result>(new CreateOrganization.Command("Fabrikam"));
        var agentur = await AssignAsync(TimelineRecordType.Organization, tagged, "Agentur");

        var result = await QueryAsync<GetOrganizations.Query, GetOrganizations.Result>(new GetOrganizations.Query(TagIds: [agentur]));

        result.Value.Items.Should().ContainSingle().Which.Tags.Should().Equal(new TagRef(agentur, "Agentur", result.Value.Items[0].Tags[0].Color));
    }

    [Fact]
    public async Task GetPipelineBoard_TaggedOpportunity_ShowsTagsOnCard()
    {
        var id = (await SendAsync<CreateOpportunity.Command, CreateOpportunity.Result>(new CreateOpportunity.Command("Azure-Migration"))).Value.Id;
        await AssignAsync(TimelineRecordType.Opportunity, id, "Cloud");

        var board = (await QueryAsync<GetPipelineBoard.Query, GetPipelineBoard.Result>(new GetPipelineBoard.Query())).Value;

        board.Columns.SelectMany(c => c.Cards).Single(c => c.Id == id).Tags.Select(t => t.Name).Should().Equal("Cloud");
    }

    private async Task<Guid> CreateContactAsync(string firstName) =>
        (await SendAsync<CreateContact.Command, CreateContact.Result>(new CreateContact.Command(firstName, "Test"))).Value.Id;

    private async Task<Guid> AssignAsync(TimelineRecordType type, Guid recordId, string tagName) =>
        (await SendAsync<AssignTag.Command, AssignTag.Result>(new AssignTag.Command(type, recordId, NewTagName: tagName))).Value.TagId;
}
