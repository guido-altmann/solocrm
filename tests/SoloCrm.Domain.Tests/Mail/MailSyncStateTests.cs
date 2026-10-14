using SoloCrm.Domain.Mail;

namespace SoloCrm.Domain.Tests.Mail;

public sealed class MailSyncStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Constructor_New_HasNoTokenYet()
    {
        var state = new MailSyncState("Graph", MailFolder.Inbox);

        state.DeltaToken.Should().BeNull();
        state.LastSyncedAt.Should().BeNull();
    }

    [Fact]
    public void Advance_ValidToken_StoresTokenAndUtcTime()
    {
        var state = new MailSyncState("Graph", MailFolder.Inbox);

        state.Advance("token-1", Now);

        state.DeltaToken.Should().Be("token-1");
        state.LastSyncedAt.Should().Be(Now);
        state.LastSyncedAt!.Value.Offset.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Advance_BlankToken_Throws(string token)
    {
        var state = new MailSyncState("Graph", MailFolder.Sent);

        var act = () => state.Advance(token, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Reset_AfterAdvance_ClearsTokenButKeepsLastSync()
    {
        var state = new MailSyncState("Graph", MailFolder.Inbox);
        state.Advance("token-1", Now);

        state.Reset();

        state.DeltaToken.Should().BeNull();
        state.LastSyncedAt.Should().Be(Now);
    }

    [Fact]
    public void Constructor_BlankProviderOrUnknownFolder_Throws()
    {
        var blank = () => new MailSyncState(" ", MailFolder.Inbox);
        var unknown = () => new MailSyncState("Graph", (MailFolder)42);

        blank.Should().Throw<ArgumentException>();
        unknown.Should().Throw<ArgumentOutOfRangeException>();
    }
}
