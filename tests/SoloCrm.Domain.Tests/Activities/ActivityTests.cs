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
}
