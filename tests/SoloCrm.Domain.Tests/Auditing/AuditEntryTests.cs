using SoloCrm.Domain.Auditing;

namespace SoloCrm.Domain.Tests.Auditing;

public sealed class AuditEntryTests
{
    [Fact]
    public void Anonymize_EntryWithChanges_RemovesValuesAndKeepsProof()
    {
        var id = Guid.CreateVersion7();
        var occurredAt = new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.Zero);
        var entry = new AuditEntry("Contact", id, AuditAction.Updated, [new AuditChange("LastName", "Lovelace", "King")], occurredAt);

        entry.Anonymize();

        entry.Changes.Should().BeEmpty();
        entry.EntityType.Should().Be("Contact");
        entry.EntityId.Should().Be(id);
        entry.Action.Should().Be(AuditAction.Updated);
        entry.OccurredAt.Should().Be(occurredAt);
    }
}
