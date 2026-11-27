using Microsoft.EntityFrameworkCore;
using Npgsql;
using SoloCrm.Domain.Enrichment;
using SoloCrm.IntegrationTests.Features;

namespace SoloCrm.IntegrationTests.Persistence;

/// <summary>Schema of the enrichment queue and its suggestions (ADR-016, SPEC 9.3).</summary>
public sealed class EnrichmentPersistenceTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    private static EnrichmentRequest NewRequest(Guid? targetId = null) =>
        EnrichmentRequest.Create(EnrichmentTargetType.Organization, targetId ?? Guid.CreateVersion7(), "FoundryAgent", Start);

    private static void Finish(EnrichmentRequest request)
    {
        request.Claim(Start, Start.AddMinutes(5));
        request.Complete(Start.AddMinutes(1));
    }

    [Fact]
    public async Task Save_SecondActiveRequestForTheSameTarget_ViolatesTheUniqueIndex()
    {
        var target = Guid.CreateVersion7();
        await using var db = OpenDb();
        db.EnrichmentRequests.Add(NewRequest(target));
        await db.SaveChangesAsync(Ct);

        db.EnrichmentRequests.Add(NewRequest(target));
        var act = () => db.SaveChangesAsync(Ct);

        var exception = await act.Should().ThrowAsync<DbUpdateException>();
        exception.Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Save_RunningRequestAlreadyExists_ViolatesTheUniqueIndex()
    {
        var target = Guid.CreateVersion7();
        var running = NewRequest(target);
        running.Claim(Start, Start.AddMinutes(5));
        await using var db = OpenDb();
        db.EnrichmentRequests.Add(running);
        await db.SaveChangesAsync(Ct);

        db.EnrichmentRequests.Add(NewRequest(target));
        var act = () => db.SaveChangesAsync(Ct);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Save_NewRequestAfterTheEarlierOnesFinished_IsAllowed()
    {
        var target = Guid.CreateVersion7();
        var completed = NewRequest(target);
        Finish(completed);
        var failed = NewRequest(target);
        failed.Claim(Start, Start.AddMinutes(5));
        failed.Fail(Start.AddMinutes(1), "Fehler");
        await using var db = OpenDb();
        db.EnrichmentRequests.AddRange(completed, failed);
        await db.SaveChangesAsync(Ct);

        db.EnrichmentRequests.Add(NewRequest(target));
        await db.SaveChangesAsync(Ct);

        (await db.EnrichmentRequests.CountAsync(r => r.TargetId == target, Ct)).Should().Be(3);
    }

    [Fact]
    public async Task Save_ActiveRequestsForDifferentTargets_AreAllowed()
    {
        await using var db = OpenDb();
        db.EnrichmentRequests.AddRange(NewRequest(), NewRequest());

        var act = () => db.SaveChangesAsync(Ct);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Save_RequestWithSuggestion_RoundTripsAllValues()
    {
        var request = NewRequest();
        request.Claim(Start, Start.AddMinutes(5));
        var suggestion = EnrichmentSuggestion.Create(
            request.Id,
            EnrichmentField.CompanyProfile,
            "**Branche:** Software",
            "alt",
            ["https://a.test/", "https://b.test/about"],
            0.73m);
        suggestion.Accept(Start.AddMinutes(2));
        await using (var db = OpenDb())
        {
            db.EnrichmentRequests.Add(request);
            db.EnrichmentSuggestions.Add(suggestion);
            await db.SaveChangesAsync(Ct);
        }

        await using var verify = OpenDb();
        var loadedRequest = await verify.EnrichmentRequests.SingleAsync(r => r.Id == request.Id, Ct);
        loadedRequest.Status.Should().Be(EnrichmentRequestStatus.Running);
        loadedRequest.Attempts.Should().Be(1);
        loadedRequest.LeaseUntil.Should().Be(Start.AddMinutes(5));
        var loaded = await verify.EnrichmentSuggestions.SingleAsync(s => s.Id == suggestion.Id, Ct);
        loaded.Field.Should().Be(EnrichmentField.CompanyProfile);
        loaded.ProposedValue.Should().Be("**Branche:** Software");
        loaded.CurrentValue.Should().Be("alt");
        loaded.SourceUrls.Should().Equal("https://a.test/", "https://b.test/about");
        loaded.Confidence.Should().Be(0.73m);
        loaded.Status.Should().Be(EnrichmentSuggestionStatus.Accepted);
        loaded.DecidedAt.Should().Be(Start.AddMinutes(2));
    }

    [Fact]
    public async Task Save_StatusAndFieldAsText_StoresTheEnumNames()
    {
        var request = NewRequest();
        await using var db = OpenDb();
        db.EnrichmentRequests.Add(request);
        db.EnrichmentSuggestions.Add(EnrichmentSuggestion.Create(request.Id, EnrichmentField.VatId, "DE123456789", null, ["https://a.test/"], 0.9m));
        await db.SaveChangesAsync(Ct);

        var status = await db.Database.SqlQuery<string>($"SELECT status AS \"Value\" FROM enrichment_requests WHERE id = {request.Id}").SingleAsync(Ct);
        var field = await db.Database.SqlQuery<string>($"SELECT field AS \"Value\" FROM enrichment_suggestions WHERE request_id = {request.Id}").SingleAsync(Ct);

        status.Should().Be("Pending");
        field.Should().Be("VatId");
    }

    [Fact]
    public async Task Delete_Request_DeletesItsSuggestions()
    {
        var request = NewRequest();
        await using var db = OpenDb();
        db.EnrichmentRequests.Add(request);
        db.EnrichmentSuggestions.Add(EnrichmentSuggestion.Create(request.Id, EnrichmentField.Email, "a@b.test", null, ["https://b.test/"], 0.5m));
        await db.SaveChangesAsync(Ct);

        await db.EnrichmentRequests.Where(r => r.Id == request.Id).ExecuteDeleteAsync(Ct);

        (await db.EnrichmentSuggestions.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task Insert_ConfidenceOutsideZeroToOne_ViolatesTheCheckConstraint()
    {
        var request = NewRequest();
        await using var db = OpenDb();
        db.EnrichmentRequests.Add(request);
        await db.SaveChangesAsync(Ct);

        var act = () => db.Database.ExecuteSqlAsync($"""
            INSERT INTO enrichment_suggestions (id, request_id, field, proposed_value, confidence, status, source_urls, created_at, updated_at)
            VALUES ({Guid.CreateVersion7()}, {request.Id}, 'Phone', '1', 1.5, 'Open', ARRAY['https://a.test/'], now(), now())
            """, Ct);

        var exception = await act.Should().ThrowAsync<PostgresException>();
        exception.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }
}
