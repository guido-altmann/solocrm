using SoloCrm.Domain.Opportunities;

namespace SoloCrm.Domain.Tests.Opportunities;

public sealed class DurationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(1000)]
    public void Create_ValueOutOfRange_Throws(int value)
    {
        var act = () => Duration.Create(value, DurationUnit.Weeks);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_UnknownUnit_Throws()
    {
        var act = () => Duration.Create(1, (DurationUnit)9);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(7, DurationUnit.Days, 7)]
    [InlineData(3, DurationUnit.Weeks, 15)]
    [InlineData(2, DurationUnit.Months, 40)]
    public void ToWorkingDays_Unit_Converts(int value, DurationUnit unit, int expected)
    {
        Duration.Create(value, unit).ToWorkingDays().Should().Be(expected);
    }

    [Theory]
    [InlineData(1, DurationUnit.Days, 1)]
    [InlineData(20, DurationUnit.Days, 1)]
    [InlineData(21, DurationUnit.Days, 2)]
    [InlineData(4, DurationUnit.Weeks, 1)]
    [InlineData(5, DurationUnit.Weeks, 2)]
    [InlineData(6, DurationUnit.Months, 6)]
    public void ToMonths_Unit_RoundsUpToWholeMonths(int value, DurationUnit unit, int expected)
    {
        Duration.Create(value, unit).ToMonths().Should().Be(expected);
    }
}
