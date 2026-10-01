using SoloCrm.Application.Features.Webhooks;

namespace SoloCrm.Application.Tests.Features.Webhooks;

public sealed class WebhookSignatureTests
{
    private const string Body = """{"id":"x"}""";
    private const long Timestamp = 1759309964;

    // Reference value computed independently: hmac.new(b"secret", b"1759309964.{\"id\":\"x\"}", sha256) in Python.
    private const string Expected = "sha256=a046b9d5729c3276ad344860352a0cd9e176c759ecb07349b65c82d2dc6ee55a";

    private static readonly DateTimeOffset SentAt = DateTimeOffset.FromUnixTimeSeconds(Timestamp);
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    [Fact]
    public void Compute_KnownInput_MatchesReferenceValue()
    {
        WebhookSignature.Compute("secret", Timestamp, Body).Should().Be(Expected);
    }

    [Fact]
    public void Verify_ValidSignatureWithinTolerance_ReturnsTrue()
    {
        WebhookSignature.Verify("secret", "1759309964", Body, Expected, SentAt + TimeSpan.FromMinutes(4), Tolerance).Should().BeTrue();
    }

    [Theory]
    [InlineData("other", "1759309964", Body)]
    [InlineData("secret", "1759309965", Body)]
    [InlineData("secret", "1759309964", """{"id":"y"}""")]
    [InlineData("secret", "not-a-number", Body)]
    public void Verify_TamperedInput_ReturnsFalse(string secret, string timestamp, string body)
    {
        WebhookSignature.Verify(secret, timestamp, body, Expected, SentAt, Tolerance).Should().BeFalse();
    }

    [Fact]
    public void Verify_OlderThanTolerance_ReturnsFalse()
    {
        WebhookSignature.Verify("secret", "1759309964", Body, Expected, SentAt + TimeSpan.FromMinutes(6), Tolerance).Should().BeFalse();
    }

    [Fact]
    public void GenerateSecret_TwoCalls_ReturnDifferent64CharHexSecrets()
    {
        var first = WebhookSignature.GenerateSecret();
        var second = WebhookSignature.GenerateSecret();

        first.Should().MatchRegex("^[0-9a-f]{64}$");
        second.Should().NotBe(first);
    }
}
