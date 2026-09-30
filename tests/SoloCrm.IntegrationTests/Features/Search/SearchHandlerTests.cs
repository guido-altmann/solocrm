using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Application.Features.Opportunities;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Domain.Organizations;
using SearchFeature = SoloCrm.Application.Features.Search.Search;

namespace SoloCrm.IntegrationTests.Features.Search;

/// <summary>Search of the command palette and the lists (US-13 AK2/AK3, ADR-007).</summary>
public sealed class SearchHandlerTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    [Fact]
    public async Task Search_Typo_FindsSimilarName()
    {
        var id = await CreateContactAsync("Max", "Schmidt");
        await CreateContactAsync("Erika", "Mustermann");

        var result = await SearchAsync("Schmitt");

        result.Contacts.Select(c => c.Id).Should().Equal(id);
    }

    [Fact]
    public async Task Search_Relevance_RanksExactAndPrefixAboveTypos()
    {
        var typo = await CreateContactAsync("Anna", "Schmitt");
        var prefix = await CreateContactAsync("Bernd", "Schmidtke");
        var exact = await CreateContactAsync("Carla", "Schmidt");

        var result = await SearchAsync("schmidt");

        result.Contacts.Select(c => c.Id).Should().Equal(exact, prefix, typo);
    }

    [Fact]
    public async Task Search_Contact_MatchesEmailAndOrganizationWithDetails()
    {
        var organizationId = await CreateOrganizationAsync("Contoso Labs");
        var byOrganization = (await SendAsync<CreateContact.Command, CreateContact.Result>(
            new CreateContact.Command("Ada", "Lovelace", "ada@example.test", OrganizationId: organizationId))).Value.Id;
        var byEmail = await CreateContactAsync("Grace", "Hopper", "grace@contoso.test");

        var result = await SearchAsync("contoso");

        result.Contacts.Should().BeEquivalentTo(
        [
            new SearchFeature.ContactHit(byOrganization, "Ada Lovelace", "ada@example.test", "Contoso Labs"),
            new SearchFeature.ContactHit(byEmail, "Grace Hopper", "grace@contoso.test", null),
        ]);
        result.Organizations.Should().Equal(new SearchFeature.OrganizationHit(organizationId, "Contoso Labs", OrganizationType.Other));
    }

    [Fact]
    public async Task Search_Opportunity_MatchesTitleWithStageAndClient()
    {
        var clientId = await CreateOrganizationAsync("Fabrikam");
        var id = (await SendAsync<CreateOpportunity.Command, CreateOpportunity.Result>(
            new CreateOpportunity.Command(".NET-Architekt Migration Azure", ClientOrganizationId: clientId))).Value.Id;

        var result = await SearchAsync("architek migraton");

        result.Opportunities.Should().Equal(new SearchFeature.OpportunityHit(id, ".NET-Architekt Migration Azure", "Neu", StageStatus.Open, "Fabrikam"));
    }

    [Fact]
    public async Task Search_Archived_AreExcluded()
    {
        var contactId = await CreateContactAsync("Linus", "Archiv");
        var organizationId = await CreateOrganizationAsync("Archiv AG");
        var opportunityId = (await SendAsync<CreateOpportunity.Command, CreateOpportunity.Result>(
            new CreateOpportunity.Command("Archiv-Projekt"))).Value.Id;
        await SendAsync<ArchiveContact.Command, ArchiveContact.Result>(new ArchiveContact.Command(contactId));
        await SendAsync<ArchiveOrganization.Command, ArchiveOrganization.Result>(new ArchiveOrganization.Command(organizationId));
        await SendAsync<ArchiveOpportunity.Command, ArchiveOpportunity.Result>(new ArchiveOpportunity.Command(opportunityId));

        var result = await SearchAsync("archiv");

        result.IsEmpty.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData(" a! ")]
    public async Task Search_LessThanTwoCharacters_ReturnsEmpty(string text)
    {
        await CreateContactAsync("Anna", "Adams");

        var result = await SearchAsync(text);

        result.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task Search_InvalidLimit_ReturnsValidationError()
    {
        var result = await QueryAsync<SearchFeature.Query, SearchFeature.Result>(new SearchFeature.Query("ab", Limit: 0));

        result.Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public async Task GetContacts_SearchWithoutSortField_OrdersByRelevanceAndToleratesTypos()
    {
        var prefix = await CreateContactAsync("Anna", "Schmidtke");
        var exact = await CreateContactAsync("Zoe", "Schmidt");
        await CreateContactAsync("Otto", "Normal");

        var byRelevance = await QueryAsync<GetContacts.Query, GetContacts.Result>(new GetContacts.Query("schmidt"));
        var typo = await QueryAsync<GetContacts.Query, GetContacts.Result>(new GetContacts.Query("Schmitt"));

        byRelevance.Value.Items.Select(c => c.Id).Should().Equal(exact, prefix);
        byRelevance.Value.TotalCount.Should().Be(2);
        typo.Value.Items.Select(c => c.Id).Should().Contain(exact);
    }

    [Fact]
    public async Task SearchOpportunities_Typo_FindsOpenRequestsFirst()
    {
        var open = (await SendAsync<CreateOpportunity.Command, CreateOpportunity.Result>(
            new CreateOpportunity.Command("Kubernetes Plattform"))).Value.Id;

        var result = await QueryAsync<SearchOpportunities.Query, SearchOpportunities.Result>(new SearchOpportunities.Query("Kubernetis"));

        result.Value.Items.Select(o => o.Id).Should().Equal(open);
    }

    private async Task<SearchFeature.Result> SearchAsync(string text) =>
        (await QueryAsync<SearchFeature.Query, SearchFeature.Result>(new SearchFeature.Query(text))).Value;

    private async Task<Guid> CreateContactAsync(string? firstName, string? lastName, string? email = null) =>
        (await SendAsync<CreateContact.Command, CreateContact.Result>(new CreateContact.Command(firstName, lastName, email))).Value.Id;

    private async Task<Guid> CreateOrganizationAsync(string name) =>
        (await SendAsync<CreateOrganization.Command, CreateOrganization.Result>(new CreateOrganization.Command(name))).Value.Id;
}
