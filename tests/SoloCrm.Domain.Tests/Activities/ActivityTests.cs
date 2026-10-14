using SoloCrm.Domain.Activities;
using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.Tests.Activities;

public sealed class ActivityTests
{
    private static readonly DateTimeOffset Yesterday = new(2026, 9, 28, 14, 30, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Log_ValidValues_NormalizesAndRaisesActivityLogged()
    {
        var contactId = Guid.CreateVersion7();
        var opportunityId = Guid.CreateVersion7();

        var activity = Activity.Log(
            ActivityType.Call,
            Yesterday,
            " Erstgespräch ",
            " **Budget** geklärt ",
            new LinkedRecords(ContactId: contactId, OpportunityId: opportunityId));

        activity.Type.Should().Be(ActivityType.Call);
        activity.OccurredAt.Should().Be(Yesterday);
        activity.OccurredAt.Offset.Should().Be(TimeSpan.Zero);
        activity.Subject.Should().Be("Erstgespräch");
        activity.Body.Should().Be("**Budget** geklärt");
        activity.ContactId.Should().Be(contactId);
        activity.OrganizationId.Should().BeNull();
        activity.OpportunityId.Should().Be(opportunityId);
        activity.DomainEvents.Should().ContainSingle().Which.Should().Be(
            new ActivityLogged(activity.Id, ActivityType.Call, Yesterday, contactId, null, opportunityId));
    }

    [Fact]
    public void Log_NoLinkedRecord_Throws()
    {
        var act = () => Activity.Log(ActivityType.Note, Yesterday, null, "Notiz", LinkedRecords.None);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Log_BlankBody_Throws(string body)
    {
        var act = () => Activity.Log(ActivityType.Note, Yesterday, "Betreff", body, new LinkedRecords(ContactId: Guid.CreateVersion7()));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Log_UnknownType_Throws()
    {
        var act = () => Activity.Log((ActivityType)42, Yesterday, null, "Notiz", new LinkedRecords(ContactId: Guid.CreateVersion7()));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Update_NewContent_KeepsLinksAndRaisesNoEvent()
    {
        var organizationId = Guid.CreateVersion7();
        var activity = Activity.Log(ActivityType.Note, Yesterday, "Alt", "Alt", new LinkedRecords(OrganizationId: organizationId));
        activity.ClearDomainEvents();

        activity.Update(ActivityType.Meeting, Yesterday.AddDays(-1), " ", "Neu");

        activity.Type.Should().Be(ActivityType.Meeting);
        activity.OccurredAt.Should().Be(Yesterday.AddDays(-1));
        activity.Subject.Should().BeNull();
        activity.Body.Should().Be("Neu");
        activity.OrganizationId.Should().Be(organizationId);
        activity.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Log_ManualActivity_HasManualOriginAndNoDirection()
    {
        var activity = Activity.Log(ActivityType.Email, Yesterday, "Betreff", "Text", new LinkedRecords(ContactId: Guid.CreateVersion7()));

        activity.Origin.Should().Be(ActivityOrigin.Manual);
        activity.Direction.Should().BeNull();
        activity.IsExcludedFromAudit(nameof(Activity.Body)).Should().BeFalse();
    }

    [Theory]
    [InlineData(ActivityDirection.Inbound)]
    [InlineData(ActivityDirection.Outbound)]
    public void LogMail_ValidValues_CreatesEmailActivityAndRaisesActivityLogged(ActivityDirection direction)
    {
        var contactId = Guid.CreateVersion7();

        var activity = Activity.LogMail(direction, Yesterday, " Angebot ", " Guten Tag ", new LinkedRecords(ContactId: contactId));

        activity.Type.Should().Be(ActivityType.Email);
        activity.Origin.Should().Be(ActivityOrigin.Mail);
        activity.Direction.Should().Be(direction);
        activity.Subject.Should().Be("Angebot");
        activity.Body.Should().Be("Guten Tag");
        activity.ContactId.Should().Be(contactId);
        activity.DomainEvents.Should().ContainSingle().Which.Should().Be(
            new ActivityLogged(activity.Id, ActivityType.Email, Yesterday, contactId, null, null));
    }

    [Fact]
    public void LogMail_UnknownDirection_Throws()
    {
        var act = () => Activity.LogMail((ActivityDirection)42, Yesterday, null, "Text", new LinkedRecords(ContactId: Guid.CreateVersion7()));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void LogMail_NoLinkedRecordOrBlankBody_Throws()
    {
        var noLink = () => Activity.LogMail(ActivityDirection.Inbound, Yesterday, null, "Text", LinkedRecords.None);
        var blank = () => Activity.LogMail(ActivityDirection.Inbound, Yesterday, null, " ", new LinkedRecords(ContactId: Guid.CreateVersion7()));

        noLink.Should().Throw<ArgumentException>();
        blank.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(nameof(Activity.Subject), true)]
    [InlineData(nameof(Activity.Body), true)]
    [InlineData(nameof(Activity.OccurredAt), false)]
    [InlineData(nameof(Activity.Direction), false)]
    public void IsExcludedFromAudit_MailActivity_ExcludesContentOnly(string field, bool expected)
    {
        var activity = Activity.LogMail(ActivityDirection.Inbound, Yesterday, "Betreff", "Text", new LinkedRecords(ContactId: Guid.CreateVersion7()));

        activity.IsExcludedFromAudit(field).Should().Be(expected);
    }

    [Fact]
    public void Update_MailActivity_KeepsOriginAndDirection()
    {
        var activity = Activity.LogMail(ActivityDirection.Outbound, Yesterday, "Alt", "Alt", new LinkedRecords(ContactId: Guid.CreateVersion7()));

        activity.Update(ActivityType.Note, Yesterday, "Neu", "Neu");

        activity.Origin.Should().Be(ActivityOrigin.Mail);
        activity.Direction.Should().Be(ActivityDirection.Outbound);
    }
}
