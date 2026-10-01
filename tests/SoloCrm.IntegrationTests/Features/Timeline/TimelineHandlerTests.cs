using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Activities;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Application.Features.Opportunities;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Application.Features.Pipeline;
using SoloCrm.Application.Features.Stages;
using SoloCrm.Application.Features.Tasks;
using SoloCrm.Application.Features.Timeline;
using SoloCrm.Domain.Activities;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Infrastructure.Persistence.Seeding;
using Kind = SoloCrm.Application.Features.Timeline.TimelineEntryKind;

namespace SoloCrm.IntegrationTests.Features.Timeline;

public sealed class TimelineHandlerTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    [Fact]
    public async Task Opportunity_History_ShowsDirectEntriesNewestFirstWithReadableChanges()
    {
        var contact = await CreateContactAsync("Max", "Mustermann");
        var id = await CreateOpportunityAsync(new CreateOpportunity.Command("Migration", DefaultStages.Applied, PrimaryContactId: contact));
        Tick();
        await SendAsync<MoveOpportunity.Command, MoveOpportunity.Result>(new MoveOpportunity.Command(id, DefaultStages.InTalks));
        Tick();
        await UpdateOpportunityAsync(id, DefaultStages.InTalks, amount: 95m, durationValue: 6, contact: contact);
        Tick();
        await UpdateOpportunityAsync(id, DefaultStages.InTalks, amount: 105m, durationValue: 6, contact: contact, remote: 80);
        Tick();
        await LogAsync(new LogActivity.Command(ActivityType.Call, "Rückruf", OpportunityId: id));
        await LogAsync(new LogActivity.Command(ActivityType.Note, "Nur beim Kontakt", ContactId: contact));
        Tick();
        var task = (await SendAsync<CreateTask.Command, CreateTask.Result>(new CreateTask.Command("Angebot schicken", OpportunityId: id))).Value.Id;
        Tick();
        await SendAsync<CompleteTask.Command, CompleteTask.Result>(new CompleteTask.Command(task));

        var timeline = await TimelineAsync(TimelineRecordType.Opportunity, id);

        timeline.Next.Should().BeNull();
        timeline.Entries.Select(e => e.Kind).Should().Equal(
            Kind.TaskCompleted, Kind.TaskCreated, Kind.Activity, Kind.Updated, Kind.Updated, Kind.Updated, Kind.Created);
        timeline.Entries.Should().AllSatisfy(e => e.Via.Should().BeNull());
        timeline.Entries[0].TaskTitle.Should().Be("Angebot schicken");
        timeline.Entries[2].Should().Match<GetTimeline.Entry>(e => e.ActivityType == ActivityType.Call && e.Body == "Rückruf");
        timeline.Entries[3].Changes.Should().Equal(new GetTimeline.Change("Preis", "95 €/h", "105 €/h"));
        timeline.Entries[4].Changes.Should().Equal(
            new GetTimeline.Change("Preis", "–", "95 €/h"),
            new GetTimeline.Change("Laufzeit", "offen", "6 Monate"));
        timeline.Entries[5].Changes.Should().Equal(new GetTimeline.Change("Phase", "Beworben", "Im Gespräch"));
    }

    [Fact]
    public async Task Opportunity_DeletedStageAndArchiving_AreShownReadably()
    {
        var closing = (await SendAsync<CreateStage.Command, CreateStage.Result>(new CreateStage.Command("Abschluss"))).Value.Id;
        var id = await CreateOpportunityAsync(new CreateOpportunity.Command("Migration"));
        Tick();
        await SendAsync<MoveOpportunity.Command, MoveOpportunity.Result>(new MoveOpportunity.Command(id, closing));
        Tick();
        await SendAsync<MoveOpportunity.Command, MoveOpportunity.Result>(new MoveOpportunity.Command(id, DefaultStages.Offer));
        (await SendAsync<DeleteStage.Command, DeleteStage.Result>(new DeleteStage.Command(closing))).IsSuccess.Should().BeTrue();
        Tick();
        await SendAsync<ArchiveOpportunity.Command, ArchiveOpportunity.Result>(new ArchiveOpportunity.Command(id));
        Tick();
        await SendAsync<RestoreOpportunity.Command, RestoreOpportunity.Result>(new RestoreOpportunity.Command(id));

        var timeline = await TimelineAsync(TimelineRecordType.Opportunity, id);

        timeline.Entries.Select(e => e.Kind).Should().Equal(Kind.Restored, Kind.Archived, Kind.Updated, Kind.Updated, Kind.Created);
        timeline.Entries[2].Changes.Should().Equal(new GetTimeline.Change("Phase", "(gelöscht)", "Angebot"));
        timeline.Entries[3].Changes.Should().Equal(new GetTimeline.Change("Phase", "Neu", "(gelöscht)"));
    }

    [Fact]
    public async Task Contact_IncludesOpportunitiesAsPrimaryContactAndHidesInvisibleChanges()
    {
        var organization = await CreateOrganizationAsync("Contoso");
        var contact = await CreateContactAsync("Max", "Mustermann");
        var other = await CreateContactAsync("Erika", "Musterfrau");
        var ownOpportunity = await CreateOpportunityAsync(new CreateOpportunity.Command("Migration", PrimaryContactId: contact));
        var otherOpportunity = await CreateOpportunityAsync(new CreateOpportunity.Command("Andere", PrimaryContactId: other));
        Tick();
        await UpdateContactAsync(contact, organization, phone: null);
        Tick();
        await UpdateContactAsync(contact, organization, phone: "+49 30 123");
        await LogAsync(new LogActivity.Command(ActivityType.Note, "Direkt", ContactId: contact));
        await LogAsync(new LogActivity.Command(ActivityType.Call, "Über Anfrage", OpportunityId: ownOpportunity));
        await LogAsync(new LogActivity.Command(ActivityType.Call, "Fremd", OpportunityId: otherOpportunity));

        var timeline = await TimelineAsync(TimelineRecordType.Contact, contact);

        timeline.Entries.Select(e => (e.Kind, e.Body ?? (e.Changes is [var first, ..] ? first.Label : null), e.Via?.Name)).Should().BeEquivalentTo(
        [
            (Kind.Activity, "Direkt", (string?)null),
            (Kind.Activity, "Über Anfrage", "Migration"),
            (Kind.Updated, "Firma", null),
            (Kind.Created, null, "Migration"),
            (Kind.Created, null, null),
        ]);
        timeline.Entries.Single(e => e.Kind == Kind.Updated).Changes.Should().Equal(new GetTimeline.Change("Firma", "–", "Contoso"));
        timeline.Entries.Single(e => e.Via is not null && e.Kind == Kind.Activity).Via!.Type.Should().Be(TimelineRecordType.Opportunity);
    }

    [Fact]
    public async Task Organization_AggregatesContactsAndOpportunitiesButNotContactCreation()
    {
        var organization = await CreateOrganizationAsync("Contoso");
        var agency = await CreateOrganizationAsync("Hays");
        var contact = (await SendAsync<CreateContact.Command, CreateContact.Result>(
            new CreateContact.Command("Max", "Mustermann", OrganizationId: organization))).Value.Id;
        var asClient = await CreateOpportunityAsync(new CreateOpportunity.Command("Als Kunde", ClientOrganizationId: organization, AgencyOrganizationId: agency));
        var asAgency = await CreateOpportunityAsync(new CreateOpportunity.Command("Als Vermittler", AgencyOrganizationId: organization));
        await CreateOpportunityAsync(new CreateOpportunity.Command("Fremd", ClientOrganizationId: agency));
        Tick();
        await LogAsync(new LogActivity.Command(ActivityType.Meeting, "Beide", ContactId: contact, OrganizationId: organization));
        await LogAsync(new LogActivity.Command(ActivityType.Call, "Kontakt", ContactId: contact));
        await LogAsync(new LogActivity.Command(ActivityType.Email, "Vermittlung", OpportunityId: asAgency));
        await SendAsync<CreateTask.Command, CreateTask.Result>(new CreateTask.Command("Nachfassen", ContactId: contact));

        var timeline = await TimelineAsync(TimelineRecordType.Organization, organization);

        timeline.Entries.Select(e => (e.Kind, e.Body ?? e.TaskTitle, e.Via?.Name)).Should().BeEquivalentTo(
        [
            (Kind.Activity, "Beide", (string?)null),
            (Kind.Activity, "Kontakt", "Max Mustermann"),
            (Kind.Activity, "Vermittlung", "Als Vermittler"),
            (Kind.TaskCreated, "Nachfassen", "Max Mustermann"),
            (Kind.Created, null, "Als Kunde"),
            (Kind.Created, null, "Als Vermittler"),
            (Kind.Created, null, null),
        ]);
        timeline.Entries.Where(e => e.Kind == Kind.Created).Select(e => e.SourceId)
            .Should().BeEquivalentTo([asClient, asAgency, organization], "the contact's creation is left out");
    }

    [Fact]
    public async Task Paging_WithCursor_ReturnsEveryEntryOnceIncludingEqualTimestamps()
    {
        var contact = await CreateContactAsync("Max", "Mustermann");
        for (var i = 0; i < 7; i++)
        {
            await LogAsync(new LogActivity.Command(ActivityType.Note, $"Notiz {i}", ContactId: contact));
            await SendAsync<CreateTask.Command, CreateTask.Result>(new CreateTask.Command($"Task {i}", ContactId: contact));
        }

        var all = (await TimelineAsync(TimelineRecordType.Contact, contact, limit: 100)).Entries;
        var paged = new List<GetTimeline.Entry>();
        GetTimeline.Cursor? cursor = null;
        do
        {
            var page = await TimelineAsync(TimelineRecordType.Contact, contact, cursor, limit: 4);
            page.Entries.Count.Should().BeLessThanOrEqualTo(4);
            paged.AddRange(page.Entries);
            cursor = page.Next;
        }
        while (cursor is not null);

        all.Should().HaveCount(15, "7 activities, 7 tasks and the creation");
        paged.Select(e => (e.Kind, e.SourceId, e.OccurredAt)).Should().Equal(all.Select(e => (e.Kind, e.SourceId, e.OccurredAt)));
    }

    [Fact]
    public async Task Get_UnknownRecordOrInvalidLimit_ReturnsErrors()
    {
        (await QueryAsync<GetTimeline.Query, GetTimeline.Result>(new GetTimeline.Query(TimelineRecordType.Contact, Guid.CreateVersion7())))
            .Error.Should().Be(TimelineErrors.NotFound);
        (await QueryAsync<GetTimeline.Query, GetTimeline.Result>(new GetTimeline.Query(TimelineRecordType.Organization, Guid.CreateVersion7(), Limit: 0)))
            .Error.Should().BeOfType<ValidationError>();
    }

    private void Tick() => Time.Advance(TimeSpan.FromMinutes(1));

    private async Task<GetTimeline.Result> TimelineAsync(TimelineRecordType type, Guid id, GetTimeline.Cursor? before = null, int limit = GetTimeline.DefaultLimit)
    {
        var result = await QueryAsync<GetTimeline.Query, GetTimeline.Result>(new GetTimeline.Query(type, id, before, limit));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    private async Task LogAsync(LogActivity.Command command) =>
        (await SendAsync<LogActivity.Command, LogActivity.Result>(command)).IsSuccess.Should().BeTrue();

    private async Task<Guid> CreateContactAsync(string firstName, string lastName) =>
        (await SendAsync<CreateContact.Command, CreateContact.Result>(new CreateContact.Command(firstName, lastName))).Value.Id;

    private async Task<Guid> CreateOrganizationAsync(string name) =>
        (await SendAsync<CreateOrganization.Command, CreateOrganization.Result>(new CreateOrganization.Command(name))).Value.Id;

    private async Task<Guid> CreateOpportunityAsync(CreateOpportunity.Command command)
    {
        var result = await SendAsync<CreateOpportunity.Command, CreateOpportunity.Result>(command);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
        return result.Value.Id;
    }

    private async Task UpdateOpportunityAsync(Guid id, Guid stage, decimal amount, int durationValue, Guid contact, int? remote = null)
    {
        var result = await SendAsync<UpdateOpportunity.Command, UpdateOpportunity.Result>(new UpdateOpportunity.Command(
            id, "Migration", stage, null, null, null, contact, PricingModel.Hourly, amount, "EUR", null,
            durationValue, DurationUnit.Months, null, remote, null));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
    }

    private async Task UpdateContactAsync(Guid id, Guid organization, string? phone)
    {
        var result = await SendAsync<UpdateContact.Command, UpdateContact.Result>(new UpdateContact.Command(
            id, "Max", "Mustermann", null, phone, null, null, organization, null, null, null));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : null);
    }
}
