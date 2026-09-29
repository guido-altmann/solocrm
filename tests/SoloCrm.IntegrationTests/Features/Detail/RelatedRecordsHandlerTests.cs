using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Application.Features.Opportunities;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Application.Features.Pipeline;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Infrastructure.Persistence.Seeding;
using Role = SoloCrm.Application.Features.Opportunities.GetRelatedOpportunities.Role;

namespace SoloCrm.IntegrationTests.Features.Detail;

/// <summary>Linked records shown on the detail views (SPEC 3.3 S5).</summary>
public sealed class RelatedRecordsHandlerTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    [Fact]
    public async Task GetRelatedOpportunities_Organization_ReturnsClientAndAgencyRolesOpenFirst()
    {
        var organization = await CreateOrganizationAsync("Contoso");
        var won = await CreateOpportunityAsync(new CreateOpportunity.Command("Gewonnen", ClientOrganizationId: organization,
            PricingModel: PricingModel.Daily, Amount: 760m));
        await SendAsync<MoveOpportunity.Command, MoveOpportunity.Result>(new MoveOpportunity.Command(won, DefaultStages.Won));
        Time.Advance(TimeSpan.FromMinutes(1));
        var agency = await CreateOpportunityAsync(new CreateOpportunity.Command("Vermittelt", AgencyOrganizationId: organization));
        await CreateOpportunityAsync(new CreateOpportunity.Command("Fremd"));

        var result = await QueryAsync<GetRelatedOpportunities.Query, GetRelatedOpportunities.Result>(
            new GetRelatedOpportunities.Query(OrganizationId: organization));

        result.Value.Items.Should().Equal(
            new GetRelatedOpportunities.Item(agency, "Vermittelt", Role.Agency, "Neu", StageStatus.Open, null, false),
            new GetRelatedOpportunities.Item(won, "Gewonnen", Role.Client, "Gewonnen", StageStatus.Won, "760 €/Tag", false));
    }

    [Fact]
    public async Task GetRelatedOpportunities_Contact_ReturnsRequestsAsPrimaryContact()
    {
        var contact = (await SendAsync<CreateContact.Command, CreateContact.Result>(new CreateContact.Command("Max", "Mustermann"))).Value.Id;
        var own = await CreateOpportunityAsync(new CreateOpportunity.Command("Migration", PrimaryContactId: contact));
        await CreateOpportunityAsync(new CreateOpportunity.Command("Fremd"));

        var result = await QueryAsync<GetRelatedOpportunities.Query, GetRelatedOpportunities.Result>(
            new GetRelatedOpportunities.Query(PrimaryContactId: contact));

        result.Value.Items.Should().ContainSingle().Which.Should().Match<GetRelatedOpportunities.Item>(i =>
            i.Id == own && i.Role == Role.PrimaryContact);
    }

    [Fact]
    public async Task GetRelatedOpportunities_NoneOrBothFilters_ReturnsValidationError()
    {
        (await QueryAsync<GetRelatedOpportunities.Query, GetRelatedOpportunities.Result>(new GetRelatedOpportunities.Query()))
            .Error.Should().BeOfType<ValidationError>();
        (await QueryAsync<GetRelatedOpportunities.Query, GetRelatedOpportunities.Result>(
                new GetRelatedOpportunities.Query(Guid.CreateVersion7(), Guid.CreateVersion7())))
            .Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public async Task GetOrganizationContacts_Organization_ReturnsActiveFirstThenByName()
    {
        var organization = await CreateOrganizationAsync("Contoso");
        var archived = await CreateContactAsync("Anna", "Alt", organization);
        await SendAsync<ArchiveContact.Command, ArchiveContact.Result>(new ArchiveContact.Command(archived));
        var zimmer = await CreateContactAsync("Zoe", "Zimmer", organization);
        var mustermann = await CreateContactAsync("Max", "Mustermann", organization);
        await CreateContactAsync("Fremd", "Person", null);

        var result = await QueryAsync<GetOrganizationContacts.Query, GetOrganizationContacts.Result>(
            new GetOrganizationContacts.Query(organization));

        result.Value.Items.Select(c => (c.Id, c.Name, c.IsArchived)).Should().Equal(
            (mustermann, "Max Mustermann", false),
            (zimmer, "Zoe Zimmer", false),
            (archived, "Anna Alt", true));
    }

    private async Task<Guid> CreateOrganizationAsync(string name) =>
        (await SendAsync<CreateOrganization.Command, CreateOrganization.Result>(new CreateOrganization.Command(name))).Value.Id;

    private async Task<Guid> CreateContactAsync(string firstName, string lastName, Guid? organization) =>
        (await SendAsync<CreateContact.Command, CreateContact.Result>(
            new CreateContact.Command(firstName, lastName, OrganizationId: organization))).Value.Id;

    private async Task<Guid> CreateOpportunityAsync(CreateOpportunity.Command command) =>
        (await SendAsync<CreateOpportunity.Command, CreateOpportunity.Result>(command)).Value.Id;
}
