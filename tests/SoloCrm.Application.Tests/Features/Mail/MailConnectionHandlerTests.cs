using FluentValidation;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Mail;

namespace SoloCrm.Application.Tests.Features.Mail;

public sealed class MailConnectionHandlerTests
{
    private static readonly MailConnectionInfo Connected = new("Graph", "guido@example.test", new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero), null, "Fehler");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetMailStatus_NotConfigured_ReportsNoConnection()
    {
        var connector = new FakeConnector { IsConfigured = false, Connection = Connected };

        var result = await new GetMailStatus.Handler(connector, new MailSyncOptions()).Handle(new GetMailStatus.Query(), Ct);

        result.Value.Should().Be(new GetMailStatus.Result(false, null, 30));
    }

    [Fact]
    public async Task GetMailStatus_ConfiguredAndConnected_ReturnsTheConnection()
    {
        var connector = new FakeConnector { IsConfigured = true, Connection = Connected };

        var result = await new GetMailStatus.Handler(connector, new MailSyncOptions { BackfillDays = 7 }).Handle(new GetMailStatus.Query(), Ct);

        result.Value.Should().Be(new GetMailStatus.Result(true, Connected, 7));
    }

    [Fact]
    public async Task GetMailStatus_ConfiguredButNotConnected_HasNoConnection()
    {
        var result = await new GetMailStatus.Handler(new FakeConnector { IsConfigured = true }, new MailSyncOptions { BackfillDays = -5 })
            .Handle(new GetMailStatus.Query(), Ct);

        result.Value.Should().Be(new GetMailStatus.Result(true, null, 0));
    }

    [Fact]
    public async Task ConnectMail_ValidCommand_ReturnsTheAccount()
    {
        var connector = new FakeConnector { IsConfigured = true, Account = "guido@example.test" };

        var result = await CreateConnectHandler(connector).Handle(new ConnectMail.Command("code", "verifier", "https://crm.example.test/mail/callback"), Ct);

        result.Value.Account.Should().Be("guido@example.test");
        connector.Connected.Should().Equal(("code", "verifier", "https://crm.example.test/mail/callback"));
    }

    [Theory]
    [InlineData(null, "v", "https://x")]
    [InlineData("c", "", "https://x")]
    [InlineData("c", "v", " ")]
    public async Task ConnectMail_MissingValue_ReturnsValidationError(string? code, string? verifier, string? redirect)
    {
        var connector = new FakeConnector { IsConfigured = true };

        var result = await CreateConnectHandler(connector).Handle(new ConnectMail.Command(code, verifier, redirect), Ct);

        result.Error.Should().BeOfType<ValidationError>();
        connector.Connected.Should().BeEmpty();
    }

    [Fact]
    public async Task ConnectMail_NotConfigured_ReturnsError()
    {
        var result = await CreateConnectHandler(new FakeConnector { IsConfigured = false })
            .Handle(new ConnectMail.Command("c", "v", "https://x"), Ct);

        result.Error.Code.Should().Be("Mail.NotConfigured");
    }

    [Fact]
    public async Task ConnectMail_MicrosoftRejects_ReturnsTheMessage()
    {
        var connector = new FakeConnector { IsConfigured = true, ConnectError = new MailSourceException("Anmeldung abgelehnt.") };

        var result = await CreateConnectHandler(connector).Handle(new ConnectMail.Command("c", "v", "https://x"), Ct);

        result.Error.Code.Should().Be("Mail.ConnectFailed");
        result.Error.Message.Should().Be("Anmeldung abgelehnt.");
    }

    [Fact]
    public async Task DisconnectMail_Always_DisconnectsTheConnector()
    {
        var connector = new FakeConnector { IsConfigured = true, Connection = Connected };

        var result = await new DisconnectMail.Handler(connector).Handle(new DisconnectMail.Command(), Ct);

        result.IsSuccess.Should().BeTrue();
        connector.Connection.Should().BeNull();
    }

    private static ConnectMail.Handler CreateConnectHandler(FakeConnector connector) =>
        new(connector, new ConnectMail.Validator());

    private sealed class FakeConnector : IMailConnector
    {
        public bool IsConfigured { get; init; }

        public MailConnectionInfo? Connection { get; set; }

        public string Account { get; init; } = "";

        public MailSourceException? ConnectError { get; init; }

        public List<(string Code, string Verifier, string RedirectUri)> Connected { get; } = [];

        public Uri BuildAuthorizationUri(string redirectUri, string state, string codeChallenge) => throw new NotSupportedException();

        public Task<string> ConnectAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken)
        {
            if (ConnectError is not null)
            {
                throw ConnectError;
            }

            Connected.Add((code, codeVerifier, redirectUri));
            return Task.FromResult(Account);
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            Connection = null;
            return Task.CompletedTask;
        }

        public Task<MailConnectionInfo?> GetConnectionAsync(CancellationToken cancellationToken) => Task.FromResult(Connection);

        public Task RecordSyncAsync(string? error, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
