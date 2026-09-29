using SoloCrm.Domain.Common;
using SoloCrm.Domain.Tasks;

namespace SoloCrm.Domain.Tests.Tasks;

public sealed class TaskItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 29);

    [Fact]
    public void Create_TitleOnly_IsOpenFreeTaskWithoutEvent()
    {
        var task = TaskItem.Create(" Angebot nachfassen ");

        task.Title.Should().Be("Angebot nachfassen");
        task.DueDate.Should().BeNull();
        task.IsCompleted.Should().BeFalse();
        task.ContactId.Should().BeNull();
        task.OrganizationId.Should().BeNull();
        task.OpportunityId.Should().BeNull();
        task.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Create_WithDueDateAndLinks_SetsValues()
    {
        var opportunityId = Guid.CreateVersion7();

        var task = TaskItem.Create("Nachfassen", Today, new LinkedRecords(OpportunityId: opportunityId));

        task.DueDate.Should().Be(Today);
        task.OpportunityId.Should().Be(opportunityId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Create_BlankTitle_Throws(string title)
    {
        var act = () => TaskItem.Create(title);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Update_NewValues_AreApplied()
    {
        var task = TaskItem.Create("Alt", Today);

        task.Update(" Neu ", null);

        task.Title.Should().Be("Neu");
        task.DueDate.Should().BeNull();
    }

    [Fact]
    public void Complete_OpenTask_SetsCompletedAtAndRaisesTaskCompleted()
    {
        var task = TaskItem.Create("Nachfassen");

        task.Complete(Now);

        task.CompletedAt.Should().Be(Now);
        task.IsCompleted.Should().BeTrue();
        task.DomainEvents.Should().ContainSingle().Which.Should().Be(new TaskCompleted(task.Id, Now));
    }

    [Fact]
    public void Complete_CompletedTask_KeepsFirstCompletionWithoutEvent()
    {
        var task = TaskItem.Create("Nachfassen");
        task.Complete(Now);
        task.ClearDomainEvents();

        task.Complete(Now.AddHours(1));

        task.CompletedAt.Should().Be(Now);
        task.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Reopen_CompletedTask_ClearsCompletedAtAndRaisesTaskReopened()
    {
        var task = TaskItem.Create("Nachfassen");
        task.Complete(Now);
        task.ClearDomainEvents();

        task.Reopen();

        task.CompletedAt.Should().BeNull();
        task.DomainEvents.Should().ContainSingle().Which.Should().Be(new TaskReopened(task.Id));
    }

    [Fact]
    public void Reopen_OpenTask_RaisesNoEvent()
    {
        var task = TaskItem.Create("Nachfassen");

        task.Reopen();

        task.DomainEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData(-1, false, true, false)]
    [InlineData(0, false, false, true)]
    [InlineData(1, false, false, false)]
    [InlineData(-1, true, false, false)]
    [InlineData(0, true, false, false)]
    public void IsOverdueAndIsDueOn_DependOnDueDateAndCompletion(int dueInDays, bool completed, bool overdue, bool dueToday)
    {
        var task = TaskItem.Create("Nachfassen", Today.AddDays(dueInDays));
        if (completed)
        {
            task.Complete(Now);
        }

        task.IsOverdue(Today).Should().Be(overdue);
        task.IsDueOn(Today).Should().Be(dueToday);
    }

    [Fact]
    public void IsOverdue_WithoutDueDate_IsFalse()
    {
        TaskItem.Create("Irgendwann").IsOverdue(Today).Should().BeFalse();
    }
}
