using SoloCrm.Domain.Webhooks;

namespace SoloCrm.Domain.Tests.Webhooks;

public sealed class WebhookSubscriptionTests
{
    private const string Secret = "protected";

    [Fact]
    public void Create_ValidInput_IsActiveWithSortedDistinctEvents()
    {
        var subscription = WebhookSubscription.Create(
            " n8n ", "https://n8n.example.test/webhook/abc", ["task.completed", "contact.created", "task.completed"], Secret);

        subscription.Name.Should().Be("n8n");
        subscription.IsActive.Should().BeTrue();
        subscription.Events.Should().Equal("contact.created", "task.completed");
        subscription.ProtectedSecret.Should().Be(Secret);
    }

    [Fact]
    public void Create_NoEvents_Throws()
    {
        var act = () => WebhookSubscription.Create("n8n", "https://n8n.example.test", [], Secret);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_UnknownEvent_Throws()
    {
        var act = () => WebhookSubscription.Create("n8n", "https://n8n.example.test", ["contact.merged"], Secret);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_RelativeUrl_Throws()
    {
        var act = () => WebhookSubscription.Create("n8n", "/webhook", ["contact.created"], Secret);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Receives_SelectedEventAfterCreation_ReturnsTrue()
    {
        var subscription = WebhookSubscription.Create("n8n", "https://n8n.example.test", ["contact.created"], Secret);

        // CreatedAt is set by the timestamp interceptor; the default is the earliest possible value.
        subscription.Receives("contact.created", DateTimeOffset.UnixEpoch).Should().BeTrue();
        subscription.Receives("task.completed", DateTimeOffset.UnixEpoch).Should().BeFalse();
    }

    [Fact]
    public void Receives_Inactive_ReturnsFalse()
    {
        var subscription = WebhookSubscription.Create("n8n", "https://n8n.example.test", ["contact.created"], Secret);

        subscription.Update("n8n", "https://n8n.example.test", ["contact.created"], isActive: false);

        subscription.Receives("contact.created", DateTimeOffset.UnixEpoch).Should().BeFalse();
    }

    [Fact]
    public void ReplaceSecret_NewSecret_ReplacesProtectedSecret()
    {
        var subscription = WebhookSubscription.Create("n8n", "https://n8n.example.test", ["contact.created"], Secret);

        subscription.ReplaceSecret("other");

        subscription.ProtectedSecret.Should().Be("other");
    }
}
