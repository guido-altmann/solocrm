using SoloCrm.Domain.Enrichment;

namespace SoloCrm.Domain.Tests.Enrichment;

public sealed class EnrichmentRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.FromHours(1));
    private static readonly DateTimeOffset Lease = Now.AddMinutes(5);

    private static EnrichmentRequest Create() =>
        EnrichmentRequest.Create(EnrichmentTargetType.Organization, Guid.CreateVersion7(), " FoundryAgent ", Now);

    private static EnrichmentRequest CreateRunning()
    {
        var request = Create();
        request.Claim(Now, Lease);
        return request;
    }

    [Fact]
    public void Create_ValidValues_IsPendingAndActive()
    {
        var request = Create();

        request.Status.Should().Be(EnrichmentRequestStatus.Pending);
        request.IsActive.Should().BeTrue();
        request.Provider.Should().Be("FoundryAgent");
        request.RequestedAt.Offset.Should().Be(TimeSpan.Zero);
        request.Attempts.Should().Be(0);
        request.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Create_EmptyTarget_Throws()
    {
        var act = () => EnrichmentRequest.Create(EnrichmentTargetType.Organization, Guid.Empty, "p", Now);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_BlankProvider_Throws(string provider)
    {
        var act = () => EnrichmentRequest.Create(EnrichmentTargetType.Organization, Guid.CreateVersion7(), provider, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_TooLongProvider_Throws()
    {
        var act = () => EnrichmentRequest.Create(
            EnrichmentTargetType.Organization, Guid.CreateVersion7(), new string('a', EnrichmentRequest.ProviderMaxLength + 1), Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Claim_PendingRequest_IsRunningWithLeaseAndCountsTheAttempt()
    {
        var request = Create();

        request.Claim(Now, Lease);

        request.Status.Should().Be(EnrichmentRequestStatus.Running);
        request.LeaseUntil.Should().Be(Lease);
        request.Attempts.Should().Be(1);
        request.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Claim_LeaseNotInTheFuture_Throws()
    {
        var request = Create();

        var act = () => request.Claim(Now, Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Claim_RunningWithValidLease_Throws()
    {
        var request = CreateRunning();

        var act = () => request.Claim(Now.AddMinutes(1), Now.AddMinutes(6));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Claim_RunningWithExpiredLease_TakesOverAndCountsAnotherAttempt()
    {
        var request = CreateRunning();

        request.Claim(Lease.AddSeconds(1), Lease.AddMinutes(5));

        request.Status.Should().Be(EnrichmentRequestStatus.Running);
        request.Attempts.Should().Be(2);
        request.LeaseUntil.Should().Be(Lease.AddMinutes(5));
    }

    [Fact]
    public void Complete_RunningRequest_IsCompletedAndClearsLeaseAndError()
    {
        var request = CreateRunning();
        request.ScheduleRetry("Timeout");
        request.Claim(Now.AddMinutes(1), Lease);

        request.Complete(Now.AddMinutes(2));

        request.Status.Should().Be(EnrichmentRequestStatus.Completed);
        request.CompletedAt.Should().Be(Now.AddMinutes(2));
        request.LeaseUntil.Should().BeNull();
        request.Error.Should().BeNull();
        request.IsActive.Should().BeFalse();
    }

    [Fact]
    public void ScheduleRetry_RunningRequest_IsPendingAgainAndKeepsTheError()
    {
        var request = CreateRunning();

        request.ScheduleRetry("Timeout");

        request.Status.Should().Be(EnrichmentRequestStatus.Pending);
        request.LeaseUntil.Should().BeNull();
        request.Error.Should().Be("Timeout");
        request.Attempts.Should().Be(1);
        request.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Fail_RunningRequest_IsFailedAndInactive()
    {
        var request = CreateRunning();

        request.Fail(Now.AddMinutes(3), "Schema verletzt");

        request.Status.Should().Be(EnrichmentRequestStatus.Failed);
        request.Error.Should().Be("Schema verletzt");
        request.CompletedAt.Should().Be(Now.AddMinutes(3));
        request.LeaseUntil.Should().BeNull();
        request.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Fail_LongOrBlankError_IsShortenedOrReplaced()
    {
        var longError = CreateRunning();
        longError.Fail(Now, new string('x', EnrichmentRequest.ErrorMaxLength + 50));
        var blankError = CreateRunning();
        blankError.Fail(Now, " ");

        longError.Error.Should().HaveLength(EnrichmentRequest.ErrorMaxLength);
        blankError.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Finish_PendingRequest_Throws()
    {
        var request = Create();

        request.Invoking(r => r.Complete(Now)).Should().Throw<InvalidOperationException>();
        request.Invoking(r => r.ScheduleRetry("x")).Should().Throw<InvalidOperationException>();
        request.Invoking(r => r.Fail(Now, "x")).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Claim_FinishedRequest_Throws()
    {
        var completed = CreateRunning();
        completed.Complete(Now);
        var failed = CreateRunning();
        failed.Fail(Now, "x");

        completed.Invoking(r => r.Claim(Now.AddHours(1), Now.AddHours(2))).Should().Throw<InvalidOperationException>();
        failed.Invoking(r => r.Claim(Now.AddHours(1), Now.AddHours(2))).Should().Throw<InvalidOperationException>();
    }
}
