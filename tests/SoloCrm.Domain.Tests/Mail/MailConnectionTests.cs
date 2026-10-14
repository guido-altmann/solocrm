using SoloCrm.Domain.Mail;

namespace SoloCrm.Domain.Tests.Mail;

public sealed class MailConnectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.FromHours(2));

    private static MailConnection Create() => new("Graph", " guido@example.test ", "protected-1", Now);

    [Fact]
    public void Constructor_ValidValues_TrimsAndStoresUtcTime()
    {
        var connection = Create();

        connection.Provider.Should().Be("Graph");
        connection.Account.Should().Be("guido@example.test");
        connection.ProtectedRefreshToken.Should().Be("protected-1");
        connection.ConnectedAt.Should().Be(Now);
        connection.ConnectedAt.Offset.Should().Be(TimeSpan.Zero);
        connection.LastSyncedAt.Should().BeNull();
        connection.LastError.Should().BeNull();
    }

    [Theory]
    [InlineData("", "a@b", "t")]
    [InlineData("Graph", " ", "t")]
    [InlineData("Graph", "a@b", "")]
    public void Constructor_BlankValue_Throws(string provider, string account, string token)
    {
        var act = () => new MailConnection(provider, account, token, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_TooLongAccount_Throws()
    {
        var act = () => new MailConnection("Graph", new string('a', MailConnection.AccountMaxLength + 1), "t", Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RecordFailure_ThenSuccess_ClearsTheError()
    {
        var connection = Create();

        connection.RecordFailure("Anmeldung abgelaufen.");
        connection.LastError.Should().Be("Anmeldung abgelaufen.");
        connection.RecordSuccess(Now.AddMinutes(2));

        connection.LastError.Should().BeNull();
        connection.LastSyncedAt.Should().Be(Now.AddMinutes(2));
    }

    [Fact]
    public void RecordFailure_LongOrBlankMessage_IsShortenedOrReplaced()
    {
        var connection = Create();

        connection.RecordFailure(new string('x', 900));
        connection.LastError.Should().HaveLength(MailConnection.ErrorMaxLength).And.EndWith("…");
        connection.RecordFailure(" ");
        connection.LastError.Should().Be("Unbekannter Fehler.");
    }

    [Fact]
    public void Reconnect_ReplacesAccountAndTokenAndClearsStatus()
    {
        var connection = Create();
        connection.RecordSuccess(Now);
        connection.RecordFailure("Fehler");

        connection.Reconnect("neu@example.test", "protected-2", Now.AddDays(1));

        connection.Account.Should().Be("neu@example.test");
        connection.ProtectedRefreshToken.Should().Be("protected-2");
        connection.ConnectedAt.Should().Be(Now.AddDays(1));
        connection.LastSyncedAt.Should().BeNull();
        connection.LastError.Should().BeNull();
    }

    [Fact]
    public void ReplaceRefreshToken_BlankToken_Throws()
    {
        var act = () => Create().ReplaceRefreshToken(" ");

        act.Should().Throw<ArgumentException>();
    }
}
