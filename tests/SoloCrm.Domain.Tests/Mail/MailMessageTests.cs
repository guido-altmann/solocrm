using SoloCrm.Domain.Mail;

namespace SoloCrm.Domain.Tests.Mail;

public sealed class MailMessageTests
{
    [Fact]
    public void Constructor_ValidValues_TrimsAndKeepsThem()
    {
        var activityId = Guid.CreateVersion7();

        var message = new MailMessage(activityId, " Graph ", " AAMk=1 ", " <abc@example.test> ", MailFolder.Sent);

        message.ActivityId.Should().Be(activityId);
        message.Provider.Should().Be("Graph");
        message.ExternalId.Should().Be("AAMk=1");
        message.InternetMessageId.Should().Be("<abc@example.test>");
        message.Folder.Should().Be(MailFolder.Sent);
        message.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Constructor_EmptyActivityId_Throws()
    {
        var act = () => new MailMessage(Guid.Empty, "Graph", "1", "<a@b>", MailFolder.Inbox);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("", "1", "<a@b>")]
    [InlineData("Graph", " ", "<a@b>")]
    [InlineData("Graph", "1", "")]
    public void Constructor_BlankValue_Throws(string provider, string externalId, string internetMessageId)
    {
        var act = () => new MailMessage(Guid.CreateVersion7(), provider, externalId, internetMessageId, MailFolder.Inbox);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_TooLongInternetMessageId_Throws()
    {
        var act = () => new MailMessage(Guid.CreateVersion7(), "Graph", "1", new string('x', MailMessage.InternetMessageIdMaxLength + 1), MailFolder.Inbox);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_UnknownFolder_Throws()
    {
        var act = () => new MailMessage(Guid.CreateVersion7(), "Graph", "1", "<a@b>", (MailFolder)42);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
