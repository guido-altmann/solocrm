using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Tasks;
using SoloCrm.Web.Components.Shared;

namespace SoloCrm.Web.Tests.Components.Shared;

public sealed class TaskListTests : BunitContext
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 9, 29);

    private readonly ICommandHandler<CompleteTask.Command, CompleteTask.Result> _complete =
        Substitute.For<ICommandHandler<CompleteTask.Command, CompleteTask.Result>>();

    private readonly ICommandHandler<ReopenTask.Command, ReopenTask.Result> _reopen =
        Substitute.For<ICommandHandler<ReopenTask.Command, ReopenTask.Result>>();

    public TaskListTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(_complete);
        Services.AddSingleton(_reopen);
        Services.AddSingleton(Substitute.For<ICommandHandler<DeleteTask.Command, DeleteTask.Result>>());
        Services.AddSingleton(new AppClock(new FakeTimeProvider(Now), TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")));
    }

    [Fact]
    public void Render_OverdueAndTodayTasks_ShowsDueDates()
    {
        var list = Render<TaskList>(p => p.Add(t => t.Tasks, [Task("Überfällig", Today.AddDays(-3)), Task("Heute", Today)]));

        var dues = list.FindAll(".task-due");
        dues[0].TextContent.Should().Contain("26.09.2026");
        dues[0].ClassList.Should().Contain("mud-error-text");
        dues[1].TextContent.Should().Contain("heute");
        dues[1].ClassList.Should().NotContain("mud-error-text");
    }

    [Fact]
    public async Task Check_Task_CompletesAndOffersUndoThatReopens()
    {
        var task = Task("Nachfassen", Today);
        _complete.Handle(new CompleteTask.Command(task.Id), Arg.Any<CancellationToken>())
            .Returns(Result<CompleteTask.Result>.Success(new CompleteTask.Result(task.Id, Now)));
        _reopen.Handle(new ReopenTask.Command(task.Id), Arg.Any<CancellationToken>())
            .Returns(Result<ReopenTask.Result>.Success(new ReopenTask.Result(task.Id)));
        var changes = 0;
        var snackbars = Render<MudSnackbarProvider>();
        var list = Render<TaskList>(p => p
            .Add(t => t.Tasks, [task])
            .Add(t => t.OnChanged, () => changes++));

        await list.Find("input[type=checkbox]").ChangeAsync(new() { Value = true });

        await _complete.Received(1).Handle(new CompleteTask.Command(task.Id), Arg.Any<CancellationToken>());
        changes.Should().Be(1);
        var undo = snackbars.WaitForElement(".mud-snackbar-action-button");
        undo.TextContent.Should().Contain("Rückgängig");

        await undo.ClickAsync(new());

        await _reopen.Received(1).Handle(new ReopenTask.Command(task.Id), Arg.Any<CancellationToken>());
        changes.Should().Be(2);
    }

    [Fact]
    public void Render_NoTasks_ShowsEmptyText()
    {
        var list = Render<TaskList>(p => p.Add(t => t.Tasks, []).Add(t => t.EmptyText, "Keine offenen Aufgaben"));

        list.Markup.Should().Contain("Keine offenen Aufgaben");
    }

    private static TaskSummary Task(string title, DateOnly? due) =>
        new(Guid.CreateVersion7(), title, due, null, null, null, null);
}
