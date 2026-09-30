using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Application.Features.Opportunities;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Application.Features.Tags;
using SoloCrm.Application.Features.Timeline;
using SoloCrm.Domain.Auditing;
using SoloCrm.Domain.Tags;

namespace SoloCrm.IntegrationTests.Features.Tags;

/// <summary>Tags (US-15, SPEC 2.3): management, inline creation, assignment with audit, deletion.</summary>
public sealed class TagHandlerTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    [Fact]
    public async Task Create_SeveralTags_AssignsPaletteColorsInTurn()
    {
        var first = await CreateTagAsync("Kunde");
        var second = await CreateTagAsync("Agentur");

        first.Color.Should().Be(TagPalette.Colors[0]);
        second.Color.Should().Be(TagPalette.Colors[1]);
    }

    [Fact]
    public async Task Create_NameDifferingOnlyInCase_ReturnsDuplicateName()
    {
        await CreateTagAsync("Kunde");

        var result = await SendAsync<CreateTag.Command, CreateTag.Result>(new CreateTag.Command(" KUNDE "));

        result.Error.Should().Be(TagErrors.DuplicateName);
    }

    [Fact]
    public async Task Insert_NameDifferingOnlyInCase_IsRejectedByUniqueIndex()
    {
        await using var db = OpenDb();
        db.Tags.Add(Tag.Create("Kunde", TagPalette.Colors[0]));
        db.Tags.Add(Tag.Create("kunde", TagPalette.Colors[1]));

        var save = () => db.SaveChangesAsync(Ct);

        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public async Task Create_InvalidName_ReturnsValidationError(string name)
    {
        var result = await SendAsync<CreateTag.Command, CreateTag.Result>(new CreateTag.Command(name));

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey(nameof(CreateTag.Command.Name));
    }

    [Fact]
    public async Task Rename_ToNameOfOtherTag_ReturnsDuplicateNameButCaseChangeIsAllowed()
    {
        var kunde = await CreateTagAsync("kunde");
        await CreateTagAsync("Agentur");

        var duplicate = await SendAsync<RenameTag.Command, RenameTag.Result>(new RenameTag.Command(kunde.Id, "agentur"));
        var caseChange = await SendAsync<RenameTag.Command, RenameTag.Result>(new RenameTag.Command(kunde.Id, "Kunde"));

        duplicate.Error.Should().Be(TagErrors.DuplicateName);
        caseChange.Value.Name.Should().Be("Kunde");
    }

    [Fact]
    public async Task ChangeColor_PaletteColor_UpdatesAndRejectsOtherColors()
    {
        var tag = await CreateTagAsync("Kunde");

        var changed = await SendAsync<ChangeTagColor.Command, ChangeTagColor.Result>(new ChangeTagColor.Command(tag.Id, TagPalette.Colors[5]));
        var invalid = await SendAsync<ChangeTagColor.Command, ChangeTagColor.Result>(new ChangeTagColor.Command(tag.Id, "#FF00FF"));

        changed.Value.Color.Should().Be(TagPalette.Colors[5]);
        invalid.Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public async Task Assign_NewTagName_CreatesTagInlineAndReusesItIgnoringCase()
    {
        var contactId = await CreateContactAsync();
        var organizationId = await CreateOrganizationAsync();

        var created = await AssignAsync(TimelineRecordType.Contact, contactId, newTagName: "Remote");
        var reused = await AssignAsync(TimelineRecordType.Organization, organizationId, newTagName: "remote");

        reused.Value.TagId.Should().Be(created.Value.TagId);
        await using var db = OpenDb();
        (await db.Tags.CountAsync(Ct)).Should().Be(1);
        (await db.ContactTags.CountAsync(Ct)).Should().Be(1);
        (await db.OrganizationTags.CountAsync(Ct)).Should().Be(1);
    }

    [Fact]
    public async Task Assign_Twice_IsIdempotent()
    {
        var opportunityId = await CreateOpportunityAsync();
        var tag = await CreateTagAsync("Azure");

        await AssignAsync(TimelineRecordType.Opportunity, opportunityId, tag.Id);
        var again = await AssignAsync(TimelineRecordType.Opportunity, opportunityId, tag.Id);

        again.IsSuccess.Should().BeTrue();
        await using var db = OpenDb();
        (await db.OpportunityTags.CountAsync(Ct)).Should().Be(1);
    }

    [Fact]
    public async Task Assign_UnknownRecordOrTag_ReturnsNotFound()
    {
        var contactId = await CreateContactAsync();
        var tag = await CreateTagAsync("Kunde");

        var unknownRecord = await AssignAsync(TimelineRecordType.Contact, Guid.CreateVersion7(), tag.Id);
        var unknownTag = await AssignAsync(TimelineRecordType.Contact, contactId, Guid.CreateVersion7());

        unknownRecord.Error.Should().Be(TagErrors.RecordNotFound);
        unknownTag.Error.Should().Be(TagErrors.NotFound);
    }

    [Theory]
    [InlineData(true, "Kunde")]
    [InlineData(false, null)]
    public async Task Assign_NeitherOrBothTagAndName_ReturnsValidationError(bool withTagId, string? newTagName)
    {
        var contactId = await CreateContactAsync();

        var result = await AssignAsync(TimelineRecordType.Contact, contactId, withTagId ? Guid.CreateVersion7() : null, newTagName);

        result.Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public async Task AssignAndRemove_Contact_AuditsTagsFieldButNotInTimeline()
    {
        var contactId = await CreateContactAsync();
        var tag = await CreateTagAsync("Kunde");

        await AssignAsync(TimelineRecordType.Contact, contactId, tag.Id);
        var removed = await SendAsync<RemoveTag.Command, RemoveTag.Result>(new RemoveTag.Command(TimelineRecordType.Contact, contactId, tag.Id));

        removed.Value.Removed.Should().BeTrue();
        await using var db = OpenDb();
        var changes = await db.AuditEntries
            .Where(a => a.EntityId == contactId && a.Action == AuditAction.Updated)
            .OrderBy(a => a.OccurredAt).ThenBy(a => a.Id)
            .Select(a => a.Changes)
            .ToListAsync(Ct);
        changes.Should().BeEquivalentTo(
            [
                new[] { new AuditChange("Tags", null, "Kunde") },
                [new AuditChange("Tags", "Kunde", null)],
            ],
            options => options.WithStrictOrdering());
        var timeline = await QueryAsync<GetTimeline.Query, GetTimeline.Result>(new GetTimeline.Query(TimelineRecordType.Contact, contactId));
        timeline.Value.Entries.Select(e => e.Kind).Should().Equal(TimelineEntryKind.Created);
    }

    [Fact]
    public async Task Remove_NotAssigned_SucceedsWithoutChange()
    {
        var contactId = await CreateContactAsync();

        var result = await SendAsync<RemoveTag.Command, RemoveTag.Result>(
            new RemoveTag.Command(TimelineRecordType.Contact, contactId, Guid.CreateVersion7()));

        result.Value.Removed.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_TagWithAssignments_RemovesThemAndAuditsEachRecord()
    {
        var contactId = await CreateContactAsync();
        var organizationId = await CreateOrganizationAsync();
        var opportunityId = await CreateOpportunityAsync();
        var tag = await CreateTagAsync("Kunde");
        await AssignAsync(TimelineRecordType.Contact, contactId, tag.Id);
        await AssignAsync(TimelineRecordType.Organization, organizationId, tag.Id);
        await AssignAsync(TimelineRecordType.Opportunity, opportunityId, tag.Id);

        var result = await SendAsync<DeleteTag.Command, DeleteTag.Result>(new DeleteTag.Command(tag.Id));

        result.Value.RemovedAssignments.Should().Be(3);
        await using var db = OpenDb();
        (await db.Tags.AnyAsync(Ct)).Should().BeFalse();
        (await db.ContactTags.AnyAsync(Ct) || await db.OrganizationTags.AnyAsync(Ct) || await db.OpportunityTags.AnyAsync(Ct))
            .Should().BeFalse();
        var removals = (await db.AuditEntries.ToListAsync(Ct))
            .Where(a => a.Changes.Contains(new AuditChange("Tags", "Kunde", null)))
            .Select(a => (a.EntityType, a.EntityId));
        removals.Should().BeEquivalentTo([("Contact", contactId), ("Organization", organizationId), ("Opportunity", opportunityId)]);
        (await db.AuditEntries.SingleAsync(a => a.EntityId == tag.Id && a.Action == AuditAction.Deleted, Ct)).EntityType.Should().Be("Tag");
    }

    [Fact]
    public async Task Delete_UnknownTag_ReturnsNotFound()
    {
        var result = await SendAsync<DeleteTag.Command, DeleteTag.Result>(new DeleteTag.Command(Guid.CreateVersion7()));

        result.Error.Should().Be(TagErrors.NotFound);
    }

    [Fact]
    public async Task GetTags_WithAssignments_ReturnsCountsPerTypeOrderedByName()
    {
        var contactId = await CreateContactAsync();
        var organizationId = await CreateOrganizationAsync();
        var remote = await CreateTagAsync("remote");
        await CreateTagAsync("Agentur");
        await AssignAsync(TimelineRecordType.Contact, contactId, remote.Id);
        await AssignAsync(TimelineRecordType.Organization, organizationId, remote.Id);

        var result = await QueryAsync<GetTags.Query, GetTags.Result>(new GetTags.Query());

        result.Value.Items.Select(t => (t.Name, t.ContactCount, t.OrganizationCount, t.OpportunityCount))
            .Should().Equal(("Agentur", 0, 0, 0), ("remote", 1, 1, 0));
    }

    [Fact]
    public async Task SearchTags_Input_ReturnsContainingTagsStartingWithInputFirst()
    {
        await CreateTagAsync("Stammkunde");
        await CreateTagAsync("Kunde");
        await CreateTagAsync("Agentur");

        var result = await QueryAsync<SearchTags.Query, SearchTags.Result>(new SearchTags.Query("kun"));

        result.Value.Items.Select(t => t.Name).Should().Equal("Kunde", "Stammkunde");
    }

    [Fact]
    public async Task GetRecordTags_Contact_ReturnsItsTagsOrderedByName()
    {
        var contactId = await CreateContactAsync();
        await AssignAsync(TimelineRecordType.Contact, contactId, newTagName: "Remote");
        await AssignAsync(TimelineRecordType.Contact, contactId, newTagName: "Azure");
        await AssignAsync(TimelineRecordType.Contact, await CreateContactAsync(), newTagName: "Andere");

        var result = await QueryAsync<GetRecordTags.Query, GetRecordTags.Result>(
            new GetRecordTags.Query(TimelineRecordType.Contact, contactId));

        result.Value.Items.Select(t => t.Name).Should().Equal("Azure", "Remote");
    }

    [Fact]
    public async Task Rename_BlankName_ReturnsValidationError()
    {
        var tag = await CreateTagAsync("Kunde");

        var result = await SendAsync<RenameTag.Command, RenameTag.Result>(new RenameTag.Command(tag.Id, " "));

        result.Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public async Task SearchTags_InvalidLimit_ReturnsValidationError()
    {
        var result = await QueryAsync<SearchTags.Query, SearchTags.Result>(new SearchTags.Query("k", Limit: 0));

        result.Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public async Task RemoveAndGetRecordTags_UnknownRecordType_ReturnValidationError()
    {
        var remove = await SendAsync<RemoveTag.Command, RemoveTag.Result>(
            new RemoveTag.Command((TimelineRecordType)99, Guid.CreateVersion7(), Guid.CreateVersion7()));
        var get = await QueryAsync<GetRecordTags.Query, GetRecordTags.Result>(
            new GetRecordTags.Query((TimelineRecordType)99, Guid.CreateVersion7()));

        remove.Error.Should().BeOfType<ValidationError>();
        get.Error.Should().BeOfType<ValidationError>();
    }

    private async Task<CreateTag.Result> CreateTagAsync(string name) =>
        (await SendAsync<CreateTag.Command, CreateTag.Result>(new CreateTag.Command(name))).Value;

    private Task<Result<AssignTag.Result>> AssignAsync(TimelineRecordType type, Guid recordId, Guid? tagId = null, string? newTagName = null) =>
        SendAsync<AssignTag.Command, AssignTag.Result>(new AssignTag.Command(type, recordId, tagId, newTagName));

    private async Task<Guid> CreateContactAsync() =>
        (await SendAsync<CreateContact.Command, CreateContact.Result>(new CreateContact.Command("Ada", "Lovelace"))).Value.Id;

    private async Task<Guid> CreateOrganizationAsync() =>
        (await SendAsync<CreateOrganization.Command, CreateOrganization.Result>(new CreateOrganization.Command("Contoso"))).Value.Id;

    private async Task<Guid> CreateOpportunityAsync() =>
        (await SendAsync<CreateOpportunity.Command, CreateOpportunity.Result>(new CreateOpportunity.Command("Azure-Migration"))).Value.Id;
}
