using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Activities;
using SoloCrm.Domain.Activities;

namespace SoloCrm.Application.Tests.Features.Activities;

public sealed class LogActivityTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    private readonly AppClock _clock = new(new FakeTimeProvider(Now), TimeZoneInfo.Utc);
    private readonly ICrmDbContextFactory _dbFactory = Substitute.For<ICrmDbContextFactory>();

    [Fact]
    public void Validator_BackdatedNoteForContact_IsValid()
    {
        var validation = new LogActivity.Validator(_clock).Validate(
            new LogActivity.Command(ActivityType.Note, "Notiz", OccurredAt: Now.AddDays(-3), ContactId: Guid.CreateVersion7()));

        validation.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validator_InvalidValues_FailsPerField()
    {
        var validation = new LogActivity.Validator(_clock).Validate(new LogActivity.Command(
            (ActivityType)42,
            " ",
            new string('x', Activity.SubjectMaxLength + 1),
            Now.AddHours(1)));

        validation.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo("Type", "Body", "Subject", "OccurredAt", "LinkedTo");
    }

    [Fact]
    public void Validator_SlightlyInTheFuture_IsTolerated()
    {
        var validation = new LogActivity.Validator(_clock).Validate(
            new LogActivity.Command(ActivityType.Call, "Anruf", OccurredAt: Now.AddMinutes(2), OpportunityId: Guid.CreateVersion7()));

        validation.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_InvalidCommand_ReturnsValidationErrorWithoutTouchingDatabase()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new LogActivity.Handler(_dbFactory, new LogActivity.Validator(_clock), _clock);

        var result = await handler.Handle(new LogActivity.Command(ActivityType.Note, "Notiz"), ct);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Keys.Should().Equal("LinkedTo");
        await _dbFactory.DidNotReceiveWithAnyArgs().CreateDbContextAsync(ct);
    }

    [Fact]
    public void UpdateValidator_MissingOccurredAtAndBody_Fails()
    {
        var validation = new UpdateActivity.Validator(_clock).Validate(
            new UpdateActivity.Command(Guid.CreateVersion7(), ActivityType.Note, null, null, ""));

        validation.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo("OccurredAt", "Body");
    }
}
