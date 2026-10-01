using SoloCrm.Domain.Common;
using SoloCrm.Domain.Contacts;

namespace SoloCrm.Domain.Tests.Common;

public sealed class DomainEventNamesTests
{
    [Theory]
    [InlineData(typeof(ContactCreated), "contact.created")]
    [InlineData(typeof(SampleRenamed), "sample.renamed")]
    [InlineData(typeof(OpportunityStageChangedSample), "opportunity.stage_changed_sample")]
    public void Of_EventType_ReturnsAggregateDotSnakeCase(Type eventType, string expected)
    {
        DomainEventNames.Of(eventType).Should().Be(expected);
    }

    private sealed record SampleRenamed;

    private sealed record OpportunityStageChangedSample;
}
