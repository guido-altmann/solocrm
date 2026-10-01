using SoloCrm.Domain.Webhooks;

namespace SoloCrm.Domain.Tests.Webhooks;

public sealed class WebhookDeliveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(200, null, true)]
    [InlineData(204, null, true)]
    [InlineData(301, "HTTP 301", false)]
    [InlineData(500, "HTTP 500", false)]
    [InlineData(null, "Timeout", false)]
    public void Record_StatusAndError_DerivesSucceeded(int? statusCode, string? error, bool expected)
    {
        var delivery = WebhookDelivery.Record(Guid.NewGuid(), Guid.NewGuid(), "contact.created", 1, statusCode, 120, error, Now);

        delivery.Succeeded.Should().Be(expected);
    }

    [Fact]
    public void Record_LongError_IsTruncated()
    {
        var delivery = WebhookDelivery.Record(Guid.NewGuid(), Guid.NewGuid(), "contact.created", 1, null, 0, new string('x', 600), Now);

        delivery.Error.Should().HaveLength(WebhookDelivery.ErrorMaxLength);
    }
}
