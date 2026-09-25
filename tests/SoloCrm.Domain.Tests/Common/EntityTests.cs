using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.Tests.Common;

public sealed class EntityTests
{
    [Fact]
    public void Constructor_NewEntity_AssignsVersion7Id()
    {
        var entity = new SampleEntity();

        entity.Id.Should().NotBeEmpty();
        entity.Id.Version.Should().Be(7);
    }

    [Fact]
    public void Constructor_TwoEntities_AssignsDistinctIds()
    {
        new SampleEntity().Id.Should().NotBe(new SampleEntity().Id);
    }

    [Fact]
    public void AddDomainEvent_Event_IsCollected()
    {
        var entity = new SampleEntity();
        var domainEvent = new SampleEvent();

        entity.Raise(domainEvent);

        entity.DomainEvents.Should().ContainSingle().Which.Should().BeSameAs(domainEvent);
    }

    [Fact]
    public void ClearDomainEvents_WithEvents_RemovesAll()
    {
        var entity = new SampleEntity();
        entity.Raise(new SampleEvent());

        entity.ClearDomainEvents();

        entity.DomainEvents.Should().BeEmpty();
    }

    private sealed class SampleEntity : Entity
    {
        public void Raise(IDomainEvent domainEvent) => AddDomainEvent(domainEvent);
    }

    private sealed record SampleEvent : IDomainEvent;
}
