using SoloCrm.Domain.Common;
using SoloCrm.Domain.Webhooks;

namespace SoloCrm.Domain.Tests.Webhooks;

public sealed class WebhookEventsTests
{
    [Fact]
    public void All_EveryDomainEvent_IsSelectable()
    {
        var eventNames = typeof(IDomainEvent).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IDomainEvent).IsAssignableFrom(t))
            .Select(DomainEventNames.Of);

        WebhookEvents.All.Should().BeEquivalentTo(eventNames);
    }

    [Fact]
    public void All_Always_ContainsSpecNames()
    {
        WebhookEvents.All.Should().Contain(["contact.created", "opportunity.stage_changed", "task.reopened", "activity.logged"]);
        WebhookEvents.All.Should().NotContain(WebhookEvents.Ping);
    }
}
