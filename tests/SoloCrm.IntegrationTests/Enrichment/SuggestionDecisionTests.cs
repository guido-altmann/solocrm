using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Enrichment;
using SoloCrm.Application.Features.Timeline;
using SoloCrm.Domain.Activities;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Enrichment;
using SoloCrm.Domain.Organizations;
using SoloCrm.IntegrationTests.Features;

namespace SoloCrm.IntegrationTests.Enrichment;

/// <summary>Accepting and rejecting suggestions, the suggestion list and the timeline hint (SOL-45).</summary>
public sealed class SuggestionDecisionTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    private const string Source = "https://www.beispiel.test/impressum";

    private static string AddressJson { get; } =
        EnrichmentAddress.From("Hauptstraße 1", null, "10115", "Berlin", null, "DE").ToJson();

    private async Task<Organization> AddOrganizationAsync(bool archived = false, string? phone = null)
    {
        var organization = Organization.Create("Beispiel GmbH", phone: phone);
        if (archived)
        {
            organization.Archive();
        }

        await using var db = OpenDb();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync(Ct);
        return organization;
    }

    private async Task<EnrichmentSuggestion> AddSuggestionAsync(
        Organization organization,
        EnrichmentField field,
        string value,
        string? currentValue = null,
        Action<EnrichmentRequest, EnrichmentSuggestion>? arrange = null,
        EnrichmentRequest? request = null)
    {
        request ??= EnrichmentRequest.Create(EnrichmentTargetType.Organization, organization.Id, "FoundryAgent", Start);
        var suggestion = EnrichmentSuggestion.Create(request.Id, field, value, currentValue, [Source, "https://handelsregister.test/x"], 0.8m);
        arrange?.Invoke(request, suggestion);

        await using var db = OpenDb();
        if (await db.EnrichmentRequests.AnyAsync(r => r.Id == request.Id, Ct) is false)
        {
            db.EnrichmentRequests.Add(request);
        }

        db.EnrichmentSuggestions.Add(suggestion);
        await db.SaveChangesAsync(Ct);
        return suggestion;
    }

    private Task<Result<AcceptSuggestion.Result>> AcceptAsync(Guid id) =>
        SendAsync<AcceptSuggestion.Command, AcceptSuggestion.Result>(new AcceptSuggestion.Command(id));

    private Task<Result<RejectSuggestion.Result>> RejectAsync(Guid id) =>
        SendAsync<RejectSuggestion.Command, RejectSuggestion.Result>(new RejectSuggestion.Command(id));

    private async Task<Organization> ReloadAsync(Guid id)
    {
        await using var db = OpenDb();
        return await db.Organizations.AsNoTracking().SingleAsync(o => o.Id == id, Ct);
    }

    private async Task<EnrichmentSuggestion> ReloadSuggestionAsync(Guid id)
    {
        await using var db = OpenDb();
        return await db.EnrichmentSuggestions.AsNoTracking().SingleAsync(s => s.Id == id, Ct);
    }

    [Fact]
    public async Task Accept_Address_ReplacesTheAddressOnly()
    {
        var organization = await AddOrganizationAsync(phone: "030 1");
        var suggestion = await AddSuggestionAsync(organization, EnrichmentField.Address, AddressJson);
        Time.Advance(TimeSpan.FromMinutes(5));

        var result = await AcceptAsync(suggestion.Id);

        result.IsSuccess.Should().BeTrue();
        var loaded = await ReloadAsync(organization.Id);
        loaded.Address.Street.Should().Be("Hauptstraße 1");
        loaded.Address.PostalCode.Should().Be("10115");
        loaded.Address.City.Should().Be("Berlin");
        loaded.Address.CountryCode.Should().Be("DE");
        loaded.Phone.Should().Be("030 1");
        loaded.Name.Should().Be("Beispiel GmbH");
        var decided = await ReloadSuggestionAsync(suggestion.Id);
        decided.Status.Should().Be(EnrichmentSuggestionStatus.Accepted);
        decided.DecidedAt.Should().Be(Start.AddMinutes(5));
    }

    [Fact]
    public async Task Accept_Phone_SetsThePhone()
    {
        var organization = await AddOrganizationAsync();
        var suggestion = await AddSuggestionAsync(organization, EnrichmentField.Phone, "+49 30 123456");

        (await AcceptAsync(suggestion.Id)).IsSuccess.Should().BeTrue();

        (await ReloadAsync(organization.Id)).Phone.Should().Be("+49 30 123456");
    }

    [Fact]
    public async Task Accept_Email_SetsTheEmail()
    {
        var organization = await AddOrganizationAsync();
        var suggestion = await AddSuggestionAsync(organization, EnrichmentField.Email, "info@beispiel.test");

        (await AcceptAsync(suggestion.Id)).IsSuccess.Should().BeTrue();

        (await ReloadAsync(organization.Id)).Email.Should().Be("info@beispiel.test");
    }

    [Theory]
    [InlineData(EnrichmentField.LegalForm, "GmbH", "LegalForm")]
    [InlineData(EnrichmentField.RegisterCourt, "Amtsgericht Berlin", "RegisterCourt")]
    [InlineData(EnrichmentField.RegisterNumber, "HRB 123", "RegisterNumber")]
    [InlineData(EnrichmentField.ManagingDirectors, "Max Mustermann", "ManagingDirectors")]
    [InlineData(EnrichmentField.VatId, "DE123456789", "VatId")]
    public async Task Accept_ExtraFieldKinds_SetTheExtraField(EnrichmentField field, string value, string key)
    {
        var organization = await AddOrganizationAsync();
        var suggestion = await AddSuggestionAsync(organization, field, value);

        (await AcceptAsync(suggestion.Id)).IsSuccess.Should().BeTrue();

        (await ReloadAsync(organization.Id)).ExtraFields.Should().Contain(key, value);
    }

    [Fact]
    public async Task Accept_CompanyProfile_CreatesANoteWithTheSources()
    {
        var organization = await AddOrganizationAsync();
        var suggestion = await AddSuggestionAsync(organization, EnrichmentField.CompanyProfile, "**Branche:** Software");
        Time.Advance(TimeSpan.FromHours(1));

        var result = await AcceptAsync(suggestion.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.ActivityId.Should().NotBeNull();
        await using var db = OpenDb();
        var note = await db.Activities.AsNoTracking().SingleAsync(a => a.Id == result.Value.ActivityId, Ct);
        note.Type.Should().Be(ActivityType.Note);
        note.Origin.Should().Be(ActivityOrigin.Enrichment);
        note.Direction.Should().BeNull();
        note.OrganizationId.Should().Be(organization.Id);
        note.OccurredAt.Should().Be(Start.AddHours(1));
        note.Subject.Should().Be("Firmenprofil");
        note.Body.Should().StartWith("**Branche:** Software").And.Contain($"- {Source}").And.Contain("- https://handelsregister.test/x");
    }

    [Fact]
    public async Task Accept_CompanyProfileAlreadyNamingTheSources_DoesNotRepeatThem()
    {
        var organization = await AddOrganizationAsync();
        var suggestion = await AddSuggestionAsync(
            organization,
            EnrichmentField.CompanyProfile,
            $"**Branche:** Software\n\nQuellen: [Impressum]({Source}), https://handelsregister.test/x");

        var result = await AcceptAsync(suggestion.Id);

        await using var db = OpenDb();
        var note = await db.Activities.AsNoTracking().SingleAsync(a => a.Id == result.Value.ActivityId, Ct);
        note.Body.Should().NotContain("**Quellen:**");
        note.Body.Split(Source).Should().HaveCount(2);
    }

    [Fact]
    public async Task Accept_CompanyProfileNamingOnlyOneSource_AddsTheMissingOne()
    {
        var organization = await AddOrganizationAsync();
        var suggestion = await AddSuggestionAsync(organization, EnrichmentField.CompanyProfile, $"Profil, siehe {Source}");

        var result = await AcceptAsync(suggestion.Id);

        await using var db = OpenDb();
        var note = await db.Activities.AsNoTracking().SingleAsync(a => a.Id == result.Value.ActivityId, Ct);
        note.Body.Split(Source).Should().HaveCount(2);
        note.Body.Should().Contain("- https://handelsregister.test/x");
    }

    [Fact]
    public async Task Accept_CompanyProfile_LeavesTheOrganizationUnchanged()
    {
        var organization = await AddOrganizationAsync(phone: "030 1");
        var suggestion = await AddSuggestionAsync(organization, EnrichmentField.CompanyProfile, "Profil");

        await AcceptAsync(suggestion.Id);

        var loaded = await ReloadAsync(organization.Id);
        loaded.Phone.Should().Be("030 1");
        loaded.ExtraFields.Should().BeEmpty();
    }

    [Fact]
    public async Task Accept_EmptyId_ReturnsValidationError()
    {
        (await AcceptAsync(Guid.Empty)).Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public async Task Accept_UnknownSuggestion_ReturnsNotFound()
    {
        (await AcceptAsync(Guid.CreateVersion7())).Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Accept_AlreadyAccepted_ReturnsConflictAndDoesNotApplyTwice()
    {
        var organization = await AddOrganizationAsync();
        var suggestion = await AddSuggestionAsync(organization, EnrichmentField.CompanyProfile, "Profil");
        await AcceptAsync(suggestion.Id);

        var again = await AcceptAsync(suggestion.Id);

        again.Error.Code.Should().Be("Enrichment.SuggestionDecided");
        again.Error.Type.Should().Be(ErrorType.Conflict);
        await using var db = OpenDb();
        (await db.Activities.CountAsync(Ct)).Should().Be(1);
    }

    [Fact]
    public async Task Accept_Rejected_ReturnsConflict()
    {
        var organization = await AddOrganizationAsync();
        var suggestion = await AddSuggestionAsync(organization, EnrichmentField.Phone, "1");
        await RejectAsync(suggestion.Id);

        (await AcceptAsync(suggestion.Id)).Error.Code.Should().Be("Enrichment.SuggestionDecided");
    }

    [Fact]
    public async Task Accept_Superseded_ReturnsSupersededConflict()
    {
        var organization = await AddOrganizationAsync();
        var suggestion = await AddSuggestionAsync(organization, EnrichmentField.Phone, "1", arrange: (_, s) => s.Supersede());

        var result = await AcceptAsync(suggestion.Id);

        result.Error.Code.Should().Be("Enrichment.SuggestionSuperseded");
        (await ReloadAsync(organization.Id)).Phone.Should().BeNull();
    }

    [Fact]
    public async Task Accept_ArchivedOrganization_ReturnsConflict()
    {
        var organization = await AddOrganizationAsync(archived: true);
        var suggestion = await AddSuggestionAsync(organization, EnrichmentField.Phone, "1");

        var result = await AcceptAsync(suggestion.Id);

        result.Error.Code.Should().Be("Enrichment.Archived");
        (await ReloadSuggestionAsync(suggestion.Id)).Status.Should().Be(EnrichmentSuggestionStatus.Open);
    }

    [Fact]
    public async Task Accept_UnreadableAddress_ReturnsErrorAndKeepsTheSuggestionOpen()
    {
        var organization = await AddOrganizationAsync();
        var suggestion = await AddSuggestionAsync(organization, EnrichmentField.Address, "keine Adresse");

        var result = await AcceptAsync(suggestion.Id);

        result.Error.Should().Be(EnrichmentSuggestionErrors.InvalidValue);
        (await ReloadSuggestionAsync(suggestion.Id)).Status.Should().Be(EnrichmentSuggestionStatus.Open);
    }

    [Fact]
    public async Task Accept_EmailWithoutAtSign_ReturnsError()
    {
        var organization = await AddOrganizationAsync();
        var suggestion = await AddSuggestionAsync(organization, EnrichmentField.Email, "info.beispiel.test");

        (await AcceptAsync(suggestion.Id)).Error.Should().Be(EnrichmentSuggestionErrors.InvalidValue);
    }

    [Fact]
    public async Task Accept_TooLongPhone_ReturnsError()
    {
        var organization = await AddOrganizationAsync();
        var suggestion = await AddSuggestionAsync(organization, EnrichmentField.Phone, new string('1', Organization.PhoneMaxLength + 1));

        (await AcceptAsync(suggestion.Id)).Error.Should().Be(EnrichmentSuggestionErrors.InvalidValue);
    }

    [Fact]
    public async Task Reject_OpenSuggestion_MarksItRejectedAndKeepsTheOrganization()
    {
        var organization = await AddOrganizationAsync();
        var suggestion = await AddSuggestionAsync(organization, EnrichmentField.Phone, "+49 1");
        Time.Advance(TimeSpan.FromMinutes(2));

        var result = await RejectAsync(suggestion.Id);

        result.IsSuccess.Should().BeTrue();
        var decided = await ReloadSuggestionAsync(suggestion.Id);
        decided.Status.Should().Be(EnrichmentSuggestionStatus.Rejected);
        decided.DecidedAt.Should().Be(Start.AddMinutes(2));
        (await ReloadAsync(organization.Id)).Phone.Should().BeNull();
    }

    [Fact]
    public async Task Reject_EmptyId_ReturnsValidationError()
    {
        (await RejectAsync(Guid.Empty)).Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public async Task Reject_UnknownSuggestion_ReturnsNotFound()
    {
        (await RejectAsync(Guid.CreateVersion7())).Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Reject_AlreadyDecided_ReturnsConflict()
    {
        var organization = await AddOrganizationAsync();
        var suggestion = await AddSuggestionAsync(organization, EnrichmentField.Phone, "1");
        await AcceptAsync(suggestion.Id);

        (await RejectAsync(suggestion.Id)).Error.Code.Should().Be("Enrichment.SuggestionDecided");
    }

    [Fact]
    public async Task Reject_Superseded_ReturnsSupersededConflict()
    {
        var organization = await AddOrganizationAsync();
        var suggestion = await AddSuggestionAsync(organization, EnrichmentField.Phone, "1", arrange: (_, s) => s.Supersede());

        (await RejectAsync(suggestion.Id)).Error.Code.Should().Be("Enrichment.SuggestionSuperseded");
    }

    [Fact]
    public async Task GetSuggestions_NeverEnriched_ReturnsNoRequestAndNoSuggestions()
    {
        var organization = await AddOrganizationAsync();

        var result = await QueryAsync<GetEnrichmentSuggestions.Query, GetEnrichmentSuggestions.Result>(new(organization.Id));

        result.Value.LatestRequest.Should().BeNull();
        result.Value.Suggestions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSuggestions_UnknownOrganization_ReturnsNotFound()
    {
        var result = await QueryAsync<GetEnrichmentSuggestions.Query, GetEnrichmentSuggestions.Result>(new(Guid.CreateVersion7()));

        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task GetSuggestions_EmptyId_ReturnsValidationError()
    {
        var result = await QueryAsync<GetEnrichmentSuggestions.Query, GetEnrichmentSuggestions.Result>(new(Guid.Empty));

        result.Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public async Task GetSuggestions_OnlyOpenOnesWithLiveValueAndReadableAddress()
    {
        var organization = await AddOrganizationAsync(phone: "030 1");
        var request = EnrichmentRequest.Create(EnrichmentTargetType.Organization, organization.Id, "FoundryAgent", Start);
        request.Claim(Start, Start.AddMinutes(5));
        request.Complete(Start.AddMinutes(1));
        await AddSuggestionAsync(organization, EnrichmentField.Address, AddressJson, request: request);
        await AddSuggestionAsync(organization, EnrichmentField.Phone, "+49 30 9", "030 1", request: request);
        await AddSuggestionAsync(organization, EnrichmentField.Email, "a@b.test", request: request, arrange: (_, s) => s.Reject(Start));
        await AddSuggestionAsync(organization, EnrichmentField.VatId, "DE1", request: request, arrange: (_, s) => s.Supersede());

        var result = await QueryAsync<GetEnrichmentSuggestions.Query, GetEnrichmentSuggestions.Result>(new(organization.Id));

        result.Value.LatestRequest!.Status.Should().Be(EnrichmentRequestStatus.Completed);
        result.Value.Suggestions.Select(s => s.Field).Should().BeEquivalentTo([EnrichmentField.Address, EnrichmentField.Phone]);
        var address = result.Value.Suggestions.Single(s => s.Field == EnrichmentField.Address);
        address.Label.Should().Be("Adresse");
        address.ProposedText.Should().Be("Hauptstraße 1, 10115 Berlin, DE");
        address.LiveText.Should().BeNull();
        address.SourceUrls.Should().Contain(Source);
        address.Confidence.Should().Be(0.8m);
        var phone = result.Value.Suggestions.Single(s => s.Field == EnrichmentField.Phone);
        phone.LiveText.Should().Be("030 1");
        phone.IsUnchanged.Should().BeFalse();
    }

    [Fact]
    public async Task GetSuggestions_ProposalEqualsLiveValue_IsMarkedUnchanged()
    {
        var organization = await AddOrganizationAsync(phone: "030 1");
        await AddSuggestionAsync(organization, EnrichmentField.Phone, "030 1");

        var result = await QueryAsync<GetEnrichmentSuggestions.Query, GetEnrichmentSuggestions.Result>(new(organization.Id));

        result.Value.Suggestions.Single().IsUnchanged.Should().BeTrue();
    }

    [Fact]
    public async Task GetSuggestions_SuggestionsOfAnotherOrganization_AreNotListed()
    {
        var organization = await AddOrganizationAsync();
        var other = await AddOrganizationAsync();
        await AddSuggestionAsync(other, EnrichmentField.Phone, "1");

        var result = await QueryAsync<GetEnrichmentSuggestions.Query, GetEnrichmentSuggestions.Result>(new(organization.Id));

        result.Value.Suggestions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSuggestions_FailedRequest_ReturnsStatusAndError()
    {
        var organization = await AddOrganizationAsync();
        var request = EnrichmentRequest.Create(EnrichmentTargetType.Organization, organization.Id, "FoundryAgent", Start);
        request.Claim(Start, Start.AddMinutes(5));
        request.Fail(Start.AddMinutes(1), "Agent nicht erreichbar");
        await using (var db = OpenDb())
        {
            db.EnrichmentRequests.Add(request);
            await db.SaveChangesAsync(Ct);
        }

        var result = await QueryAsync<GetEnrichmentSuggestions.Query, GetEnrichmentSuggestions.Result>(new(organization.Id));

        result.Value.LatestRequest!.Status.Should().Be(EnrichmentRequestStatus.Failed);
        result.Value.LatestRequest.Error.Should().Be("Agent nicht erreichbar");
    }

    [Fact]
    public async Task Timeline_AcceptedField_ShowsAnEnrichedEntryWithSourceAndOldAndNewValue()
    {
        var organization = await AddOrganizationAsync();
        var suggestion = await AddSuggestionAsync(organization, EnrichmentField.Phone, "+49 30 1", "030 1");
        Time.Advance(TimeSpan.FromMinutes(3));
        await AcceptAsync(suggestion.Id);

        var result = await QueryAsync<GetTimeline.Query, GetTimeline.Result>(new(TimelineRecordType.Organization, organization.Id));

        var entry = result.Value.Entries.Single(e => e.Kind == TimelineEntryKind.Enriched);
        entry.OccurredAt.Should().Be(Start.AddMinutes(3));
        entry.EnrichedFrom.Should().Be("beispiel.test, handelsregister.test");
        entry.Via.Should().BeNull();
        entry.Changes.Should().ContainSingle().Which.Should().Be(new GetTimeline.Change("Telefon", "030 1", "+49 30 1"));
    }

    [Fact]
    public async Task Timeline_AcceptedFieldWithoutOldValue_ShowsAPlaceholder()
    {
        var organization = await AddOrganizationAsync();
        var suggestion = await AddSuggestionAsync(organization, EnrichmentField.Address, AddressJson);
        await AcceptAsync(suggestion.Id);

        var result = await QueryAsync<GetTimeline.Query, GetTimeline.Result>(new(TimelineRecordType.Organization, organization.Id));

        result.Value.Entries.Single(e => e.Kind == TimelineEntryKind.Enriched).Changes!
            .Should().ContainSingle().Which.Should().Be(new GetTimeline.Change("Adresse", "–", "Hauptstraße 1, 10115 Berlin, DE"));
    }

    [Fact]
    public async Task Timeline_RejectedOrOpenSuggestion_ShowsNoEnrichedEntry()
    {
        var organization = await AddOrganizationAsync();
        var request = EnrichmentRequest.Create(EnrichmentTargetType.Organization, organization.Id, "FoundryAgent", Start);
        var rejected = await AddSuggestionAsync(organization, EnrichmentField.Phone, "1", request: request);
        await AddSuggestionAsync(organization, EnrichmentField.Email, "a@b.test", request: request);
        await RejectAsync(rejected.Id);

        var result = await QueryAsync<GetTimeline.Query, GetTimeline.Result>(new(TimelineRecordType.Organization, organization.Id));

        result.Value.Entries.Should().NotContain(e => e.Kind == TimelineEntryKind.Enriched);
    }

    [Fact]
    public async Task Timeline_AcceptedProfile_ShowsOnlyTheNoteWithOrigin()
    {
        var organization = await AddOrganizationAsync();
        var suggestion = await AddSuggestionAsync(organization, EnrichmentField.CompanyProfile, "Profil");
        await AcceptAsync(suggestion.Id);

        var result = await QueryAsync<GetTimeline.Query, GetTimeline.Result>(new(TimelineRecordType.Organization, organization.Id));

        result.Value.Entries.Should().NotContain(e => e.Kind == TimelineEntryKind.Enriched);
        var note = result.Value.Entries.Single(e => e.Kind == TimelineEntryKind.Activity);
        note.Origin.Should().Be(ActivityOrigin.Enrichment);
        note.ActivityType.Should().Be(ActivityType.Note);
    }

    [Fact]
    public async Task Timeline_ManyEnrichedEntries_ArePagedWithoutLossOrDuplicates()
    {
        var organization = await AddOrganizationAsync();
        var request = EnrichmentRequest.Create(EnrichmentTargetType.Organization, organization.Id, "FoundryAgent", Start);
        var fields = new[] { EnrichmentField.LegalForm, EnrichmentField.RegisterCourt, EnrichmentField.RegisterNumber, EnrichmentField.VatId, EnrichmentField.ManagingDirectors };
        foreach (var field in fields)
        {
            await AddSuggestionAsync(organization, field, "x", request: request, arrange: (_, s) => s.Accept(Start));
        }

        var seen = new List<Guid>();
        GetTimeline.Cursor? cursor = null;
        do
        {
            var page = (await QueryAsync<GetTimeline.Query, GetTimeline.Result>(new(TimelineRecordType.Organization, organization.Id, cursor, 2))).Value;
            seen.AddRange(page.Entries.Where(e => e.Kind == TimelineEntryKind.Enriched).Select(e => e.Position.Id));
            cursor = page.Next;
        }
        while (cursor is not null);

        seen.Should().HaveCount(5).And.OnlyHaveUniqueItems();
    }
}
