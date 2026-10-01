using SoloCrm.Application.Features.Webhooks;

namespace SoloCrm.Application.Tests.Features.Webhooks;

public sealed class WebhookTargetsTests
{
    private readonly WebhookTargets _targets = new([" n8n ", "192.168.1.10", ""]);

    [Theory]
    [InlineData("https://n8n.example.test/webhook/abc", true)]
    [InlineData("https://10.0.0.5/webhook", true)]
    [InlineData("http://n8n:5678/webhook/abc", true)]
    [InlineData("http://N8N:5678/webhook/abc", true)]
    [InlineData("http://192.168.1.10/webhook", true)]
    [InlineData("http://n8n.example.test/webhook", false)]
    [InlineData("https://user:pass@n8n.example.test/webhook", false)]
    [InlineData("ftp://n8n/webhook", false)]
    [InlineData("/webhook", false)]
    [InlineData(null, false)]
    public void IsAllowed_Url_FollowsSchemeAndHostRules(string? url, bool expected)
    {
        _targets.IsAllowed(url).Should().Be(expected);
    }

    [Fact]
    public void HttpsOnly_HttpUrl_IsNotAllowed()
    {
        WebhookTargets.HttpsOnly.IsAllowed("http://localhost/webhook").Should().BeFalse();
    }
}
