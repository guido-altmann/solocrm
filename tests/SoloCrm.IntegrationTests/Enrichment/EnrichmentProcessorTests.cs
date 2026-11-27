using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Enrichment;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Enrichment;
using SoloCrm.Domain.Organizations;
using SoloCrm.Infrastructure.Enrichment;
using SoloCrm.Infrastructure.Persistence;
using SoloCrm.IntegrationTests.Features;

namespace SoloCrm.IntegrationTests.Enrichment;

public sealed class EnrichmentProcessorTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    private readonly FakeProvider _provider = new();

    private EnrichmentProcessor CreateProcessor(EnrichmentProcessingOptions? options = null) => new(
        Get<IDbContextFactory<CrmDbContext>>(),
        _provider,
        Time,
        Options.Create(options ?? new EnrichmentProcessingOptions()),
        NullLogger<EnrichmentProcessor>.Instance);

    private async Task<Organization> AddOrganizationAsync(string name = "Beispiel GmbH", string? website = "https://www.beispiel.test")
    {
        var organization = Organization.Create(name, website: website, phone: "030 111");
        await using var db = OpenDb();
        db.Organizations.Add(organization);
        await db.SaveChangesAsync(Ct);
        return organization;
    }

    private async Task<EnrichmentRequest> AddRequestAsync(Guid targetId, Action<EnrichmentRequest>? arrange = null)
    {
        var request = EnrichmentRequest.Create(EnrichmentTargetType.Organization, targetId, "FoundryAgent", Start);
        arrange?.Invoke(request);
        await using var db = OpenDb();
        db.EnrichmentRequests.Add(request);
        await db.SaveChangesAsync(Ct);
        return request;
    }

    private async Task<EnrichmentRequest> LoadAsync(Guid requestId)
    {
        await using var db = OpenDb();
        return await db.EnrichmentRequests.AsNoTracking().SingleAsync(r => r.Id == requestId, Ct);
    }

    private static ProposedValue Phone(string value = "+49 30 1234567") =>
        new(EnrichmentField.Phone, value, ["https://beispiel.test/impressum"], 0.9m);

    [Fact]
    public async Task ProcessDue_NothingDue_ReturnsZeroAndDoesNotCallTheProvider()
    {
        var processed = await CreateProcessor().ProcessDueAsync(Ct);

        processed.Should().Be(0);
        _provider.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessDue_PendingRequest_StoresSuggestionsWithCurrentValuesAndCompletes()
    {
        var organization = await AddOrganizationAsync();
        var request = await AddRequestAsync(organization.Id);
        _provider.Result = [
            Phone(),
            new ProposedValue(EnrichmentField.VatId, "DE123456789", ["https://beispiel.test/impressum"], 0.8m),
            new ProposedValue(EnrichmentField.Address, EnrichmentAddress.From("Weg 1", null, "10115", "Berlin", null, "DE").ToJson(), ["https://beispiel.test/"], 0.7m)];

        var processed = await CreateProcessor().ProcessDueAsync(Ct);

        processed.Should().Be(1);
        _provider.Calls.Should().ContainSingle().Which.Should().Be(new EnrichmentSubject(organization.Id, "Beispiel GmbH", "beispiel.test"));
        var loaded = await LoadAsync(request.Id);
        loaded.Status.Should().Be(EnrichmentRequestStatus.Completed);
        loaded.Attempts.Should().Be(1);
        loaded.LeaseUntil.Should().BeNull();
        loaded.CompletedAt.Should().Be(Start);
        await using var db = OpenDb();
        var suggestions = await db.EnrichmentSuggestions.Where(s => s.RequestId == request.Id).ToListAsync(Ct);
        suggestions.Should().HaveCount(3).And.OnlyContain(s => s.Status == EnrichmentSuggestionStatus.Open);
        suggestions.Single(s => s.Field == EnrichmentField.Phone).CurrentValue.Should().Be("030 111");
        suggestions.Single(s => s.Field == EnrichmentField.Phone).ProposedValue.Should().Be("+49 30 1234567");
        suggestions.Single(s => s.Field == EnrichmentField.VatId).CurrentValue.Should().BeNull();
        suggestions.Single(s => s.Field == EnrichmentField.Address).CurrentValue.Should().BeNull();
    }

    [Fact]
    public async Task ProcessDue_OrganizationWithValues_StoresThemAsCurrentValues()
    {
        var organization = Organization.Create(
            "Beispiel GmbH", address: Address.Create("Alt 1", null, "10115", "Berlin", null, "DE"), email: "alt@beispiel.test");
        organization.SetExtraField(EnrichmentFieldKeys.VatId, "DE000");
        await using (var arrange = OpenDb())
        {
            arrange.Organizations.Add(organization);
            await arrange.SaveChangesAsync(Ct);
        }

        var request = await AddRequestAsync(organization.Id);
        _provider.Result = [
            new ProposedValue(EnrichmentField.Email, "neu@beispiel.test", ["https://beispiel.test/"], 0.9m),
            new ProposedValue(EnrichmentField.VatId, "DE123", ["https://beispiel.test/"], 0.9m),
            new ProposedValue(EnrichmentField.Address, EnrichmentAddress.From("Neu 2", null, "10117", "Berlin", null, "DE").ToJson(), ["https://beispiel.test/"], 0.9m)];

        await CreateProcessor().ProcessDueAsync(Ct);

        await using var db = OpenDb();
        var suggestions = await db.EnrichmentSuggestions.Where(s => s.RequestId == request.Id).ToListAsync(Ct);
        suggestions.Single(s => s.Field == EnrichmentField.Email).CurrentValue.Should().Be("alt@beispiel.test");
        suggestions.Single(s => s.Field == EnrichmentField.VatId).CurrentValue.Should().Be("DE000");
        EnrichmentAddress.TryParse(suggestions.Single(s => s.Field == EnrichmentField.Address).CurrentValue!)!.Street.Should().Be("Alt 1");
    }

    [Fact]
    public async Task ProcessDue_NoWebsite_PassesNoDomain()
    {
        var organization = await AddOrganizationAsync(website: null);
        await AddRequestAsync(organization.Id);

        await CreateProcessor().ProcessDueAsync(Ct);

        _provider.Calls.Single().Domain.Should().BeNull();
    }

    [Fact]
    public async Task ProcessDue_NothingFound_CompletesWithoutSuggestions()
    {
        var organization = await AddOrganizationAsync();
        var request = await AddRequestAsync(organization.Id);

        await CreateProcessor().ProcessDueAsync(Ct);

        (await LoadAsync(request.Id)).Status.Should().Be(EnrichmentRequestStatus.Completed);
        await using var db = OpenDb();
        (await db.EnrichmentSuggestions.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task ProcessDue_UnstorableSuggestion_IsSkippedAndTheRestKept()
    {
        var organization = await AddOrganizationAsync();
        var request = await AddRequestAsync(organization.Id);
        _provider.Result = [
            new ProposedValue(EnrichmentField.Email, "a@beispiel.test", ["https://beispiel.test/" + new string('x', EnrichmentSuggestion.SourceUrlMaxLength)], 0.9m),
            Phone()];

        await CreateProcessor().ProcessDueAsync(Ct);

        await using var db = OpenDb();
        (await db.EnrichmentSuggestions.Where(s => s.RequestId == request.Id).Select(s => s.Field).ToListAsync(Ct)).Should().Equal(EnrichmentField.Phone);
        (await LoadAsync(request.Id)).Status.Should().Be(EnrichmentRequestStatus.Completed);
    }

    [Fact]
    public async Task ProcessDue_RunningWithValidLease_IsNotClaimed()
    {
        var organization = await AddOrganizationAsync();
        var request = await AddRequestAsync(organization.Id, r => r.Claim(Start, Start.AddMinutes(5)));
        Time.Advance(TimeSpan.FromMinutes(4));

        var processed = await CreateProcessor().ProcessDueAsync(Ct);

        processed.Should().Be(0);
        (await LoadAsync(request.Id)).Status.Should().Be(EnrichmentRequestStatus.Running);
    }

    [Fact]
    public async Task ProcessDue_RunningWithExpiredLease_IsTakenOver()
    {
        var organization = await AddOrganizationAsync();
        var request = await AddRequestAsync(organization.Id, r => r.Claim(Start, Start.AddMinutes(5)));
        Time.Advance(TimeSpan.FromMinutes(6));
        _provider.Result = [Phone()];

        var processed = await CreateProcessor().ProcessDueAsync(Ct);

        processed.Should().Be(1);
        var loaded = await LoadAsync(request.Id);
        loaded.Status.Should().Be(EnrichmentRequestStatus.Completed);
        loaded.Attempts.Should().Be(2);
    }

    [Fact]
    public async Task ProcessDue_InterruptedOnEveryAttempt_FailsWithoutCallingTheProvider()
    {
        var organization = await AddOrganizationAsync();
        var request = await AddRequestAsync(organization.Id, r =>
        {
            r.Claim(Start, Start.AddMinutes(5));
            r.ScheduleRetry("x");
            r.Claim(Start, Start.AddMinutes(5));
            r.ScheduleRetry("x");
            r.Claim(Start, Start.AddMinutes(5));
        });
        Time.Advance(TimeSpan.FromMinutes(6));

        var processed = await CreateProcessor().ProcessDueAsync(Ct);

        processed.Should().Be(0);
        _provider.Calls.Should().BeEmpty();
        var loaded = await LoadAsync(request.Id);
        loaded.Status.Should().Be(EnrichmentRequestStatus.Failed);
        loaded.Error.Should().Contain("unterbrochen");
        loaded.LeaseUntil.Should().BeNull();
    }

    [Fact]
    public async Task ProcessDue_TransientFailure_RetriesUntilTheLastAttemptThenFails()
    {
        var organization = await AddOrganizationAsync();
        var request = await AddRequestAsync(organization.Id);
        _provider.Failure = new EnrichmentProviderException("Der Agent ist überlastet.", isTransient: true);
        var processor = CreateProcessor();

        await processor.ProcessDueAsync(Ct);
        var afterFirst = await LoadAsync(request.Id);
        await processor.ProcessDueAsync(Ct);
        var afterSecond = await LoadAsync(request.Id);
        await processor.ProcessDueAsync(Ct);
        var afterThird = await LoadAsync(request.Id);
        var fourth = await processor.ProcessDueAsync(Ct);

        afterFirst.Status.Should().Be(EnrichmentRequestStatus.Pending);
        afterFirst.Error.Should().Be("Der Agent ist überlastet.");
        afterFirst.Attempts.Should().Be(1);
        afterSecond.Status.Should().Be(EnrichmentRequestStatus.Pending);
        afterThird.Status.Should().Be(EnrichmentRequestStatus.Failed);
        afterThird.Attempts.Should().Be(3);
        afterThird.Error.Should().Be("Der Agent ist überlastet.");
        fourth.Should().Be(0);
        _provider.Calls.Should().HaveCount(3);
    }

    [Fact]
    public async Task ProcessDue_TransientFailureThenSuccess_Completes()
    {
        var organization = await AddOrganizationAsync();
        var request = await AddRequestAsync(organization.Id);
        _provider.Failure = new EnrichmentProviderException("Kurz weg.", isTransient: true);
        var processor = CreateProcessor();
        await processor.ProcessDueAsync(Ct);
        _provider.Failure = null;
        _provider.Result = [Phone()];

        await processor.ProcessDueAsync(Ct);

        var loaded = await LoadAsync(request.Id);
        loaded.Status.Should().Be(EnrichmentRequestStatus.Completed);
        loaded.Error.Should().BeNull();
        loaded.Attempts.Should().Be(2);
    }

    [Fact]
    public async Task ProcessDue_NonTransientFailure_FailsAtOnceWithTheMessage()
    {
        var organization = await AddOrganizationAsync();
        var request = await AddRequestAsync(organization.Id);
        _provider.Failure = new EnrichmentProviderException("Der Zugriff auf den Agent wurde abgelehnt.");

        await CreateProcessor().ProcessDueAsync(Ct);

        var loaded = await LoadAsync(request.Id);
        loaded.Status.Should().Be(EnrichmentRequestStatus.Failed);
        loaded.Error.Should().Be("Der Zugriff auf den Agent wurde abgelehnt.");
        loaded.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task ProcessDue_ProviderTakesTooLong_TimesOutAndRetries()
    {
        var organization = await AddOrganizationAsync();
        var request = await AddRequestAsync(organization.Id);
        _provider.Hang = true;
        var options = new EnrichmentProcessingOptions { RunTimeout = TimeSpan.FromMinutes(3) };

        var running = CreateProcessor(options).ProcessDueAsync(Ct);
        await _provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Time.Advance(TimeSpan.FromMinutes(3));
        await running;

        var loaded = await LoadAsync(request.Id);
        loaded.Status.Should().Be(EnrichmentRequestStatus.Pending);
        loaded.Error.Should().Contain("nicht rechtzeitig");
    }

    [Fact]
    public async Task ProcessDue_StoppedWhileRunning_ThrowsAndLeavesTheRequestToTheLease()
    {
        var organization = await AddOrganizationAsync();
        var request = await AddRequestAsync(organization.Id);
        _provider.Hang = true;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var running = CreateProcessor().ProcessDueAsync(stop.Token);
        await _provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await stop.CancelAsync();

        await running.Invoking(r => r).Should().ThrowAsync<OperationCanceledException>();
        var loaded = await LoadAsync(request.Id);
        loaded.Status.Should().Be(EnrichmentRequestStatus.Running);
        loaded.LeaseUntil.Should().Be(Start.AddMinutes(5));
    }

    [Fact]
    public async Task ProcessDue_UnexpectedException_LeavesTheRequestRunningForTheLease()
    {
        var organization = await AddOrganizationAsync();
        var request = await AddRequestAsync(organization.Id);
        _provider.Crash = new InvalidOperationException("Bug");

        var processed = await CreateProcessor().ProcessDueAsync(Ct);

        processed.Should().Be(1);
        (await LoadAsync(request.Id)).Status.Should().Be(EnrichmentRequestStatus.Running);
    }

    [Fact]
    public async Task ProcessDue_OrganizationGone_FailsWithoutCallingTheProvider()
    {
        var request = await AddRequestAsync(Guid.CreateVersion7());

        await CreateProcessor().ProcessDueAsync(Ct);

        _provider.Calls.Should().BeEmpty();
        var loaded = await LoadAsync(request.Id);
        loaded.Status.Should().Be(EnrichmentRequestStatus.Failed);
        loaded.Error.Should().Contain("Organisation");
    }

    [Fact]
    public async Task ProcessDue_ArchivedOrganization_Fails()
    {
        var organization = Organization.Create("Alt GmbH");
        organization.Archive();
        await using (var arrange = OpenDb())
        {
            arrange.Organizations.Add(organization);
            await arrange.SaveChangesAsync(Ct);
        }

        var request = await AddRequestAsync(organization.Id);

        await CreateProcessor().ProcessDueAsync(Ct);

        (await LoadAsync(request.Id)).Status.Should().Be(EnrichmentRequestStatus.Failed);
        _provider.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessDue_NewRequestCompletes_SupersedesOnlyOlderOpenSuggestionsOfTheSameOrganization()
    {
        var organization = await AddOrganizationAsync();
        var other = await AddOrganizationAsync("Andere GmbH");
        var oldOpen = await AddCompletedRequestWithSuggestionsAsync(organization.Id, accept: false);
        var oldAccepted = await AddCompletedRequestWithSuggestionsAsync(organization.Id, accept: true);
        var otherOpen = await AddCompletedRequestWithSuggestionsAsync(other.Id, accept: false);
        var request = await AddRequestAsync(organization.Id);
        _provider.Result = [Phone()];

        await CreateProcessor().ProcessDueAsync(Ct);

        await using var db = OpenDb();
        (await db.EnrichmentSuggestions.SingleAsync(s => s.Id == oldOpen, Ct)).Status.Should().Be(EnrichmentSuggestionStatus.Superseded);
        (await db.EnrichmentSuggestions.SingleAsync(s => s.Id == oldAccepted, Ct)).Status.Should().Be(EnrichmentSuggestionStatus.Accepted);
        (await db.EnrichmentSuggestions.SingleAsync(s => s.Id == otherOpen, Ct)).Status.Should().Be(EnrichmentSuggestionStatus.Open);
        (await db.EnrichmentSuggestions.Where(s => s.RequestId == request.Id).ToListAsync(Ct)).Should().ContainSingle().Which.IsOpen.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessDue_NewRequestFinds_NothingStillSupersedesTheOlderOnes()
    {
        var organization = await AddOrganizationAsync();
        var oldOpen = await AddCompletedRequestWithSuggestionsAsync(organization.Id, accept: false);
        await AddRequestAsync(organization.Id);

        await CreateProcessor().ProcessDueAsync(Ct);

        await using var db = OpenDb();
        (await db.EnrichmentSuggestions.SingleAsync(s => s.Id == oldOpen, Ct)).Status.Should().Be(EnrichmentSuggestionStatus.Superseded);
    }

    [Fact]
    public async Task ProcessDue_FailedRequest_KeepsOlderSuggestionsOpen()
    {
        var organization = await AddOrganizationAsync();
        var oldOpen = await AddCompletedRequestWithSuggestionsAsync(organization.Id, accept: false);
        await AddRequestAsync(organization.Id);
        _provider.Failure = new EnrichmentProviderException("Nein.");

        await CreateProcessor().ProcessDueAsync(Ct);

        await using var db = OpenDb();
        (await db.EnrichmentSuggestions.SingleAsync(s => s.Id == oldOpen, Ct)).Status.Should().Be(EnrichmentSuggestionStatus.Open);
    }

    [Fact]
    public async Task ProcessDue_RequestWasFinishedMeanwhile_DiscardsTheResult()
    {
        var organization = await AddOrganizationAsync();
        var request = await AddRequestAsync(organization.Id);
        _provider.Result = [Phone()];
        _provider.OnCall = async () =>
        {
            await using var db = OpenDb();
            var running = await db.EnrichmentRequests.SingleAsync(r => r.Id == request.Id, Ct);
            running.Complete(Start);
            await db.SaveChangesAsync(Ct);
        };

        await CreateProcessor().ProcessDueAsync(Ct);

        await using var verify = OpenDb();
        (await verify.EnrichmentSuggestions.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task ProcessDue_TwoProcessorsAtOnce_RunTheRequestOnlyOnce()
    {
        var organization = await AddOrganizationAsync();
        await AddRequestAsync(organization.Id);
        _provider.Hang = true;
        var first = CreateProcessor().ProcessDueAsync(Ct);
        await _provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        var second = await CreateProcessor().ProcessDueAsync(Ct);
        _provider.Hang = false;
        _provider.Release.TrySetResult();
        var firstClaimed = await first;

        second.Should().Be(0);
        firstClaimed.Should().Be(1);
        _provider.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task ProcessDue_SeveralRequests_RunsOldestFirstOnePerCall()
    {
        var first = await AddOrganizationAsync("Eins");
        var second = await AddOrganizationAsync("Zwei");
        await AddRequestAsync(second.Id, _ => { });
        await using (var db = OpenDb())
        {
            var older = EnrichmentRequest.Create(EnrichmentTargetType.Organization, first.Id, "FoundryAgent", Start.AddMinutes(-5));
            db.EnrichmentRequests.Add(older);
            await db.SaveChangesAsync(Ct);
        }

        var processor = CreateProcessor();
        await processor.ProcessDueAsync(Ct);
        await processor.ProcessDueAsync(Ct);

        _provider.Calls.Select(c => c.Name).Should().Equal("Eins", "Zwei");
    }

    private async Task<Guid> AddCompletedRequestWithSuggestionsAsync(Guid targetId, bool accept)
    {
        var request = EnrichmentRequest.Create(EnrichmentTargetType.Organization, targetId, "FoundryAgent", Start.AddDays(-1));
        request.Claim(Start.AddDays(-1), Start.AddDays(-1).AddMinutes(5));
        request.Complete(Start.AddDays(-1));
        var suggestion = EnrichmentSuggestion.Create(request.Id, EnrichmentField.Email, "alt@beispiel.test", null, ["https://beispiel.test/"], 0.5m);
        if (accept)
        {
            suggestion.Accept(Start.AddDays(-1));
        }

        await using var db = OpenDb();
        db.EnrichmentRequests.Add(request);
        db.EnrichmentSuggestions.Add(suggestion);
        await db.SaveChangesAsync(Ct);
        return suggestion.Id;
    }

    private sealed class FakeProvider : IEnrichmentProvider
    {
        public string Name => "FoundryAgent";

        public IReadOnlyList<ProposedValue> Result { get; set; } = [];

        public EnrichmentProviderException? Failure { get; set; }

        public Exception? Crash { get; set; }

        public bool Hang { get; set; }

        public Func<Task>? OnCall { get; set; }

        public List<EnrichmentSubject> Calls { get; } = [];

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<ProposedValue>> EnrichAsync(EnrichmentSubject subject, CancellationToken cancellationToken)
        {
            Calls.Add(subject);
            Started.TrySetResult();
            if (Hang)
            {
                await Release.Task.WaitAsync(cancellationToken);
            }

            if (OnCall is not null)
            {
                await OnCall();
            }

            return Crash is not null ? throw Crash : Failure is not null ? throw Failure : Result;
        }
    }
}
