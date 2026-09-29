using SoloCrm.Domain.Opportunities;

namespace SoloCrm.Domain.Tests.Opportunities;

public sealed class StageTests
{
    [Fact]
    public void Create_ValidValues_TrimsName()
    {
        var stage = Stage.Create(" Angebot ", 4, StageStatus.Open);

        stage.Name.Should().Be("Angebot");
        stage.SortOrder.Should().Be(4);
        stage.Status.Should().Be(StageStatus.Open);
    }

    [Fact]
    public void Create_BlankName_Throws()
    {
        var act = () => Stage.Create(" ", 1, StageStatus.Open);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_UnknownStatus_Throws()
    {
        var act = () => Stage.Create("Neu", 1, (StageStatus)7);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RenameAndMoveTo_NewValues_AreApplied()
    {
        var stage = Stage.Create("Neu", 1, StageStatus.Open);

        stage.Rename("Eingang");
        stage.MoveTo(3);

        stage.Name.Should().Be("Eingang");
        stage.SortOrder.Should().Be(3);
    }
}
