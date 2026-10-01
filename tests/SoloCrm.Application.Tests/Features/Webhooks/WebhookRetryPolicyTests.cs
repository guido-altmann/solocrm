using SoloCrm.Application.Features.Webhooks;

namespace SoloCrm.Application.Tests.Features.Webhooks;

public sealed class WebhookRetryPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 5)]
    [InlineData(3, 30)]
    [InlineData(4, 120)]
    [InlineData(5, 720)]
    public void NextAttemptAt_FailedAttempt_GrowsExponentially(int failedAttempts, int expectedMinutes)
    {
        WebhookRetryPolicy.NextAttemptAt(failedAttempts, Now).Should().Be(Now.AddMinutes(expectedMinutes));
    }

    [Fact]
    public void NextAttemptAt_SixthFailedAttempt_GivesUp()
    {
        WebhookRetryPolicy.NextAttemptAt(WebhookRetryPolicy.MaxAttempts, Now).Should().BeNull();
    }

    [Fact]
    public void Delays_Always_CoverAllRetries()
    {
        WebhookRetryPolicy.Delays.Should().HaveCount(WebhookRetryPolicy.MaxAttempts - 1).And.BeInAscendingOrder();
    }
}
