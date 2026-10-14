using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Mail;
using SoloCrm.Infrastructure.Mail;
using SoloCrm.IntegrationTests.Features;

namespace SoloCrm.IntegrationTests.Mail;

public sealed class GraphAuthenticatorTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    private const string TokenJson = """{ "access_token": "access-1", "refresh_token": "refresh-1", "expires_in": 3600 }""";
    private const string RedirectUri = "https://crm.example.test/mail/callback";

    private readonly StubHttp _http = new();

    public override async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await base.DisposeAsync();
    }

    private GraphAuthenticator CreateAuthenticator(GraphMailOptions? options = null) => new(
        _http,
        Get<Microsoft.EntityFrameworkCore.IDbContextFactory<SoloCrm.Infrastructure.Persistence.CrmDbContext>>(),
        Get<ISecretProtector>(),
        Options.Create(options ?? Configured()),
        Time,
        NullLogger<GraphAuthenticator>.Instance);

    private static GraphMailOptions Configured() => new() { TenantId = "tenant-1.example.test", ClientId = "client-1", ClientSecret = "secret-1" };

    private async Task<string> StoreConnectionAsync(string account = "guido@example.test", string refreshToken = "refresh-0")
    {
        var protectedToken = Get<ISecretProtector>().Protect(refreshToken);
        await using var db = OpenDb();
        db.MailConnections.Add(new MailConnection(GraphAuthenticator.Provider, account, protectedToken, Start));
        await db.SaveChangesAsync(Ct);
        return protectedToken;
    }

    [Fact]
    public void IsConfigured_RequiresTenantClientAndSecret()
    {
        CreateAuthenticator(new GraphMailOptions()).IsConfigured.Should().BeFalse();
        CreateAuthenticator(new GraphMailOptions { TenantId = "t", ClientId = "c" }).IsConfigured.Should().BeFalse();
        CreateAuthenticator(new GraphMailOptions { TenantId = "../evil", ClientId = "c", ClientSecret = "s" }).IsConfigured.Should().BeFalse();
        CreateAuthenticator().IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void BuildAuthorizationUri_ContainsClientScopeStateAndPkce()
    {
        var uri = CreateAuthenticator().BuildAuthorizationUri(RedirectUri, "state-1", "challenge-1");

        uri.Host.Should().Be("login.microsoftonline.com");
        uri.AbsolutePath.Should().Be("/tenant-1.example.test/oauth2/v2.0/authorize");
        uri.Query.Should().Contain("client_id=client-1")
            .And.Contain("response_type=code")
            .And.Contain("redirect_uri=https%3A%2F%2Fcrm.example.test%2Fmail%2Fcallback")
            .And.Contain("scope=offline_access%20Mail.Read%20User.Read")
            .And.Contain("state=state-1")
            .And.Contain("code_challenge=challenge-1")
            .And.Contain("code_challenge_method=S256");
    }

    [Fact]
    public void BuildAuthorizationUri_NotConfigured_Throws()
    {
        var act = () => CreateAuthenticator(new GraphMailOptions()).BuildAuthorizationUri(RedirectUri, "s", "c");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Connect_ValidCode_StoresEncryptedRefreshTokenAndReturnsAccount()
    {
        _http.Respond(HttpStatusCode.OK, TokenJson);
        _http.Respond(HttpStatusCode.OK, """{ "mail": "guido@example.test", "userPrincipalName": "guido@tenant.example.test" }""");

        var account = await CreateAuthenticator().ConnectAsync("code-1", "verifier-1", RedirectUri, Ct);

        account.Should().Be("guido@example.test");
        var tokenRequest = _http.Requests[0];
        tokenRequest.Uri.AbsoluteUri.Should().Be("https://login.microsoftonline.com/tenant-1.example.test/oauth2/v2.0/token");
        tokenRequest.Method.Should().Be(HttpMethod.Post);
        tokenRequest.Body.Should().Contain("grant_type=authorization_code")
            .And.Contain("code=code-1")
            .And.Contain("code_verifier=verifier-1")
            .And.Contain("client_secret=secret-1");
        _http.Requests[1].Authorization.Should().Be("Bearer access-1");
        await using var db = OpenDb();
        var connection = await db.MailConnections.SingleAsync(Ct);
        connection.Provider.Should().Be("Graph");
        connection.Account.Should().Be("guido@example.test");
        connection.ProtectedRefreshToken.Should().NotContain("refresh-1");
        Get<ISecretProtector>().Unprotect(connection.ProtectedRefreshToken).Should().Be("refresh-1");
        connection.ConnectedAt.Should().Be(Start);
    }

    [Fact]
    public async Task Connect_AccountWithoutMail_FallsBackToUserPrincipalName()
    {
        _http.Respond(HttpStatusCode.OK, TokenJson);
        _http.Respond(HttpStatusCode.OK, """{ "mail": null, "userPrincipalName": "guido@tenant.example.test" }""");

        var account = await CreateAuthenticator().ConnectAsync("code-1", "verifier-1", RedirectUri, Ct);

        account.Should().Be("guido@tenant.example.test");
    }

    [Fact]
    public async Task Connect_RejectedCode_ThrowsAndStoresNothing()
    {
        _http.Respond(HttpStatusCode.BadRequest, """{ "error": "invalid_grant", "error_description": "AADSTS70008" }""");

        var act = () => CreateAuthenticator().ConnectAsync("bad", "verifier-1", RedirectUri, Ct);

        (await act.Should().ThrowAsync<MailSourceException>()).Which.Message.Should().Contain("neu verbinden");
        await using var db = OpenDb();
        (await db.MailConnections.AnyAsync(Ct)).Should().BeFalse();
    }

    [Fact]
    public async Task Connect_WrongClientSecret_ExplainsTheConfiguration()
    {
        _http.Respond(HttpStatusCode.Unauthorized, """{ "error": "invalid_client" }""");

        var act = () => CreateAuthenticator().ConnectAsync("code", "verifier", RedirectUri, Ct);

        (await act.Should().ThrowAsync<MailSourceException>()).Which.Message.Should().Contain("invalid_client").And.Contain("Client-Secret");
    }

    [Fact]
    public async Task Connect_NoRefreshToken_Throws()
    {
        _http.Respond(HttpStatusCode.OK, """{ "access_token": "access-1", "expires_in": 3600 }""");

        var act = () => CreateAuthenticator().ConnectAsync("code", "verifier", RedirectUri, Ct);

        (await act.Should().ThrowAsync<MailSourceException>()).Which.Message.Should().Contain("offline_access");
    }

    [Fact]
    public async Task Connect_OtherAccount_ResetsSyncProgress_SameAccountKeepsIt()
    {
        await StoreConnectionAsync("alt@example.test");
        await using (var db = OpenDb())
        {
            var state = new MailSyncState("Graph", MailFolder.Inbox);
            state.Advance("token", Start);
            db.MailSyncStates.Add(state);
            await db.SaveChangesAsync(Ct);
        }

        _http.Respond(HttpStatusCode.OK, TokenJson);
        _http.Respond(HttpStatusCode.OK, """{ "mail": "alt@example.test" }""");
        await CreateAuthenticator().ConnectAsync("code", "verifier", RedirectUri, Ct);
        await using (var db = OpenDb())
        {
            (await db.MailSyncStates.CountAsync(Ct)).Should().Be(1);
        }

        _http.Respond(HttpStatusCode.OK, TokenJson);
        _http.Respond(HttpStatusCode.OK, """{ "mail": "neu@example.test" }""");
        await CreateAuthenticator().ConnectAsync("code", "verifier", RedirectUri, Ct);

        await using var verify = OpenDb();
        (await verify.MailSyncStates.CountAsync(Ct)).Should().Be(0);
        (await verify.MailConnections.SingleAsync(Ct)).Account.Should().Be("neu@example.test");
    }

    [Fact]
    public async Task GetAccessToken_UsesRefreshTokenAndCachesTheAccessToken()
    {
        await StoreConnectionAsync(refreshToken: "refresh-0");
        _http.Respond(HttpStatusCode.OK, """{ "access_token": "access-1", "refresh_token": "refresh-0", "expires_in": 3600 }""");
        var authenticator = CreateAuthenticator();

        var first = await authenticator.GetAccessTokenAsync(Ct);
        var second = await authenticator.GetAccessTokenAsync(Ct);

        first.Should().Be("access-1");
        second.Should().Be("access-1");
        _http.Requests.Should().ContainSingle().Which.Body.Should().Contain("grant_type=refresh_token").And.Contain("refresh_token=refresh-0");
    }

    [Fact]
    public async Task GetAccessToken_AfterExpiry_RefreshesAndStoresRotatedRefreshToken()
    {
        await StoreConnectionAsync(refreshToken: "refresh-0");
        _http.Respond(HttpStatusCode.OK, """{ "access_token": "access-1", "refresh_token": "refresh-1", "expires_in": 3600 }""");
        _http.Respond(HttpStatusCode.OK, """{ "access_token": "access-2", "refresh_token": "refresh-2", "expires_in": 3600 }""");
        var authenticator = CreateAuthenticator();
        await authenticator.GetAccessTokenAsync(Ct);

        Time.Advance(TimeSpan.FromMinutes(59).Add(TimeSpan.FromSeconds(30)));
        var token = await authenticator.GetAccessTokenAsync(Ct);

        token.Should().Be("access-2");
        _http.Requests[1].Body.Should().Contain("refresh_token=refresh-1");
        await using var db = OpenDb();
        Get<ISecretProtector>().Unprotect((await db.MailConnections.SingleAsync(Ct)).ProtectedRefreshToken).Should().Be("refresh-2");
    }

    [Fact]
    public async Task GetAccessToken_NoConnection_Throws()
    {
        var act = () => CreateAuthenticator().GetAccessTokenAsync(Ct);

        (await act.Should().ThrowAsync<MailSourceException>()).Which.Message.Should().Contain("kein Postfach");
    }

    [Fact]
    public async Task GetAccessToken_RevokedGrant_AsksToReconnect()
    {
        await StoreConnectionAsync();
        _http.Respond(HttpStatusCode.BadRequest, """{ "error": "invalid_grant" }""");

        var act = () => CreateAuthenticator().GetAccessTokenAsync(Ct);

        (await act.Should().ThrowAsync<MailSourceException>()).Which.Message.Should().Contain("neu verbinden");
    }

    [Fact]
    public async Task GetAccessToken_UnreadableStoredToken_AsksToReconnect()
    {
        await using (var db = OpenDb())
        {
            db.MailConnections.Add(new MailConnection("Graph", "guido@example.test", "CfDJ8-not-a-valid-payload", Start));
            await db.SaveChangesAsync(Ct);
        }

        var act = () => CreateAuthenticator().GetAccessTokenAsync(Ct);

        (await act.Should().ThrowAsync<MailSourceException>()).Which.Message.Should().Contain("nicht lesbar");
        _http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Disconnect_RemovesConnectionAndProgressAndForgetsTheAccessToken()
    {
        await StoreConnectionAsync();
        await using (var db = OpenDb())
        {
            db.MailSyncStates.Add(new MailSyncState("Graph", MailFolder.Sent));
            await db.SaveChangesAsync(Ct);
        }

        _http.Respond(HttpStatusCode.OK, TokenJson);
        var authenticator = CreateAuthenticator();
        await authenticator.GetAccessTokenAsync(Ct);

        await authenticator.DisconnectAsync(Ct);

        (await authenticator.GetConnectionAsync(Ct)).Should().BeNull();
        await using var verify = OpenDb();
        (await verify.MailSyncStates.AnyAsync(Ct)).Should().BeFalse();
        var act = () => authenticator.GetAccessTokenAsync(Ct);
        await act.Should().ThrowAsync<MailSourceException>();
    }

    [Fact]
    public async Task RecordSync_SuccessAndFailure_AreVisibleInTheConnectionInfo()
    {
        await StoreConnectionAsync();
        var authenticator = CreateAuthenticator();

        Time.Advance(TimeSpan.FromMinutes(5));
        await authenticator.RecordSyncAsync(null, Ct);
        var ok = await authenticator.GetConnectionAsync(Ct);
        await authenticator.RecordSyncAsync("Fehler", Ct);
        var failed = await authenticator.GetConnectionAsync(Ct);

        ok.Should().Be(new MailConnectionInfo("Graph", "guido@example.test", Start, Start.AddMinutes(5), null));
        failed!.LastError.Should().Be("Fehler");
        failed.LastSyncedAt.Should().Be(Start.AddMinutes(5));
    }

    [Fact]
    public async Task RecordSync_WithoutConnection_DoesNothing()
    {
        var act = () => CreateAuthenticator().RecordSyncAsync("Fehler", Ct);

        await act.Should().NotThrowAsync();
    }
}
