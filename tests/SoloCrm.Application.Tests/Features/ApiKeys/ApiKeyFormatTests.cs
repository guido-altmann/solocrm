using SoloCrm.Application.Features.ApiKeys;

namespace SoloCrm.Application.Tests.Features.ApiKeys;

public sealed class ApiKeyFormatTests
{
    [Fact]
    public void Generate_Always_ReturnsWellFormedKeyWithMatchingPrefixAndHash()
    {
        var generated = ApiKeyFormat.Generate();

        generated.Key.Should().MatchRegex("^scrm_[a-z2-9]{8}_[0-9a-f]{64}$");
        generated.Key.Should().StartWith($"scrm_{generated.Prefix}_");
        generated.Hash.Should().Be(ApiKeyFormat.Hash(generated.Key)).And.MatchRegex("^[0-9a-f]{64}$");
        ApiKeyFormat.TryGetPrefix(generated.Key, out var prefix).Should().BeTrue();
        prefix.Should().Be(generated.Prefix);
    }

    [Fact]
    public void Generate_TwoCalls_ReturnDifferentKeys()
    {
        ApiKeyFormat.Generate().Key.Should().NotBe(ApiKeyFormat.Generate().Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer abc")]
    [InlineData("scrm_ab12cd34")]
    [InlineData("xxxx_ab12cd34_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("scrm_AB12CD34_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("scrm_ab12cd34_0123456789ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("scrm_ab12cd34_0123456789abcdef")]
    [InlineData("scrm_ab12cd34_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef_x")]
    public void TryGetPrefix_MalformedKey_ReturnsFalse(string? key)
    {
        ApiKeyFormat.TryGetPrefix(key, out _).Should().BeFalse();
    }

    [Fact]
    public void Hash_KnownKey_IsSha256Hex()
    {
        // Reference: printf 'scrm_test' | shasum -a 256
        ApiKeyFormat.Hash("scrm_test").Should().Be("819a041ecc0255f244d2e8dfb9c160ddb77c2beef9d3ed1c1697c139cf1f403a");
        ApiKeyFormat.HashesEqual(ApiKeyFormat.Hash("a"), ApiKeyFormat.Hash("a")).Should().BeTrue();
        ApiKeyFormat.HashesEqual(ApiKeyFormat.Hash("a"), ApiKeyFormat.Hash("b")).Should().BeFalse();
    }

    [Fact]
    public void Display_Prefix_HidesSecret()
    {
        ApiKeyFormat.Display("ab12cd34").Should().Be("scrm_ab12cd34_…");
    }
}
