using SoloCrm.Domain.ApiKeys;

namespace SoloCrm.Domain.Tests.ApiKeys;

public sealed class ApiKeyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_ValidInput_IsActiveAndUnused()
    {
        var key = ApiKey.Create(" n8n ", "ab12cd34", "hash");

        key.Name.Should().Be("n8n");
        key.IsRevoked.Should().BeFalse();
        key.LastUsedAt.Should().BeNull();
    }

    [Theory]
    [InlineData("", "ab12cd34")]
    [InlineData("n8n", "short")]
    public void Create_InvalidInput_Throws(string name, string prefix)
    {
        var act = () => ApiKey.Create(name, prefix, "hash");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Revoke_Twice_KeepsFirstTime()
    {
        var key = ApiKey.Create("n8n", "ab12cd34", "hash");

        key.Revoke(Now);
        key.Revoke(Now.AddHours(1));

        key.RevokedAt.Should().Be(Now);
        key.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public void RecordUse_WithinPrecision_ChangesNothing()
    {
        var key = ApiKey.Create("n8n", "ab12cd34", "hash");

        key.RecordUse(Now).Should().BeTrue();
        key.RecordUse(Now.AddSeconds(59)).Should().BeFalse();
        key.LastUsedAt.Should().Be(Now);
        key.RecordUse(Now.AddMinutes(1)).Should().BeTrue();
        key.LastUsedAt.Should().Be(Now.AddMinutes(1));
    }
}
