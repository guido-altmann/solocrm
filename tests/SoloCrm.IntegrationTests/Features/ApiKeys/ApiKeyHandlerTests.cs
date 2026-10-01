using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.ApiKeys;

namespace SoloCrm.IntegrationTests.Features.ApiKeys;

/// <summary>API keys (US-17 AK1/AK2): only the hash is stored, revoked keys are rejected.</summary>
public sealed class ApiKeyHandlerTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    [Fact]
    public async Task Create_ValidName_ReturnsKeyOnceAndStoresOnlyItsHash()
    {
        var result = await SendAsync<CreateApiKey.Command, CreateApiKey.Result>(new CreateApiKey.Command(" n8n "));

        result.IsSuccess.Should().BeTrue();
        await using var db = OpenDb();
        var stored = await db.ApiKeys.SingleAsync(Ct);
        stored.Name.Should().Be("n8n");
        stored.Prefix.Should().Be(result.Value.Prefix);
        stored.KeyHash.Should().Be(ApiKeyFormat.Hash(result.Value.Key));
        stored.KeyHash.Should().NotContain(result.Value.Key[14..]);
        stored.CreatedAt.Should().Be(Start);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public async Task Create_InvalidName_ReturnsValidationError(string? name)
    {
        var result = await SendAsync<CreateApiKey.Command, CreateApiKey.Result>(new CreateApiKey.Command(name));

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Name");
    }

    [Fact]
    public async Task Authenticate_ValidKey_ReturnsKeyAndUpdatesLastUsedThrottled()
    {
        var created = await CreateAsync("n8n");

        var first = await AuthenticateAsync(created.Key);
        Time.Advance(TimeSpan.FromSeconds(30));
        await AuthenticateAsync(created.Key);

        first.Value.ApiKeyId.Should().Be(created.Id);
        first.Value.Name.Should().Be("n8n");
        await using (var db = OpenDb())
        {
            (await db.ApiKeys.SingleAsync(Ct)).LastUsedAt.Should().Be(Start);
        }

        Time.Advance(TimeSpan.FromSeconds(30));
        await AuthenticateAsync(created.Key);
        await using var verify = OpenDb();
        (await verify.ApiKeys.SingleAsync(Ct)).LastUsedAt.Should().Be(Start.AddMinutes(1));
    }

    [Fact]
    public async Task Authenticate_WrongSecretOrMalformedOrUnknownKey_ReturnsInvalid()
    {
        var created = await CreateAsync("n8n");
        var wrongSecret = created.Key[..^1] + (created.Key[^1] == '0' ? '1' : '0');
        var unknownPrefix = $"scrm_zzzzzzzz_{created.Key[14..]}";

        (await AuthenticateAsync(wrongSecret)).Error.Should().Be(ApiKeyErrors.Invalid);
        (await AuthenticateAsync(unknownPrefix)).Error.Should().Be(ApiKeyErrors.Invalid);
        (await AuthenticateAsync("garbage")).Error.Should().Be(ApiKeyErrors.Invalid);
        (await AuthenticateAsync(null)).Error.Should().Be(ApiKeyErrors.Invalid);
        await using var db = OpenDb();
        (await db.ApiKeys.SingleAsync(Ct)).LastUsedAt.Should().BeNull();
    }

    [Fact]
    public async Task Revoke_ActiveKey_RejectsItAfterwards()
    {
        var created = await CreateAsync("n8n");
        Time.Advance(TimeSpan.FromHours(1));

        var revoked = await SendAsync<RevokeApiKey.Command, RevokeApiKey.Result>(new RevokeApiKey.Command(created.Id));

        revoked.Value.RevokedAt.Should().Be(Start.AddHours(1));
        (await AuthenticateAsync(created.Key)).Error.Should().Be(ApiKeyErrors.Invalid);
    }

    [Fact]
    public async Task Revoke_UnknownOrEmptyId_ReturnsError()
    {
        var unknown = await SendAsync<RevokeApiKey.Command, RevokeApiKey.Result>(new RevokeApiKey.Command(Guid.NewGuid()));
        var empty = await SendAsync<RevokeApiKey.Command, RevokeApiKey.Result>(new RevokeApiKey.Command(Guid.Empty));

        unknown.Error.Should().Be(ApiKeyErrors.NotFound);
        empty.Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public async Task GetApiKeys_ActiveAndRevoked_ListsActiveFirstWithoutSecrets()
    {
        var old = await CreateAsync("alt");
        Time.Advance(TimeSpan.FromMinutes(1));
        await CreateAsync("n8n");
        Time.Advance(TimeSpan.FromMinutes(1));
        await CreateAsync("zapier");
        await SendAsync<RevokeApiKey.Command, RevokeApiKey.Result>(new RevokeApiKey.Command(old.Id));

        var result = await QueryAsync<GetApiKeys.Query, GetApiKeys.Result>(new GetApiKeys.Query());

        result.Value.Items.Select(k => k.Name).Should().Equal("zapier", "n8n", "alt");
        result.Value.Items[2].IsRevoked.Should().BeTrue();
        result.Value.Items[2].Display.Should().Be($"scrm_{old.Prefix}_…");
    }

    private async Task<CreateApiKey.Result> CreateAsync(string name) =>
        (await SendAsync<CreateApiKey.Command, CreateApiKey.Result>(new CreateApiKey.Command(name))).Value;

    private Task<Result<AuthenticateApiKey.Result>> AuthenticateAsync(string? key) =>
        SendAsync<AuthenticateApiKey.Command, AuthenticateApiKey.Result>(new AuthenticateApiKey.Command(key));
}
