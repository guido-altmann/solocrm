using SoloCrm.Domain.Contacts;
using SoloCrm.Infrastructure.Persistence.Outbox;

namespace SoloCrm.IntegrationTests.Persistence;

public sealed class OutboxMessageTests
{
    [Theory]
    [InlineData(typeof(ContactCreated), "contact.created")]
    [InlineData(typeof(ProbeRenamed), "probe.renamed")]
    [InlineData(typeof(OpportunityStageChangedSample), "opportunity.stage_changed_sample")]
    public void EventTypeName_EventType_ReturnsAggregateDotSnakeCase(Type eventType, string expected)
    {
        OutboxMessage.EventTypeName(eventType).Should().Be(expected);
    }

    private sealed record OpportunityStageChangedSample;
}
