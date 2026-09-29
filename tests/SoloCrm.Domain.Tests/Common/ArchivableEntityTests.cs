using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.Tests.Common;

public sealed class ArchivableEntityTests
{
    [Fact]
    public void Constructor_NewEntity_IsNotArchived()
    {
        new SampleEntity().IsArchived.Should().BeFalse();
    }

    [Fact]
    public void Archive_ActiveEntity_SetsIsArchived()
    {
        var entity = new SampleEntity();

        entity.Archive();

        entity.IsArchived.Should().BeTrue();
    }

    [Fact]
    public void Restore_ArchivedEntity_ClearsIsArchived()
    {
        var entity = new SampleEntity();
        entity.Archive();

        entity.Restore();

        entity.IsArchived.Should().BeFalse();
    }

    private sealed class SampleEntity : ArchivableEntity;
}
