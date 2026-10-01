using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Activities;
using SoloCrm.Application.Features.Stages;
using SoloCrm.Application.Features.Tasks;
using SoloCrm.Domain.Activities;

namespace SoloCrm.Web.Endpoints;

/// <summary><c>POST /activities</c>, <c>/tasks</c> and <c>GET /stages</c> (SPEC 5).</summary>
internal static class ActivityTaskStageEndpoints
{
    private static readonly string[] TaskPatchFields = ["title", "dueDate", "completed"];

    public static void MapActivityEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/activities", CreateActivityAsync)
            .WithTags("Aktivitäten")
            .WithSummary("Aktivität erfassen (mindestens ein Bezug: contactId, organizationId oder opportunityId)")
            .Produces<CreatedResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    public static void MapTaskEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/tasks").WithTags("Aufgaben");

        group.MapGet("/{id:guid}", GetTaskAsync)
            .WithSummary("Aufgabe lesen")
            .Produces<TaskDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateTaskAsync)
            .WithSummary("Aufgabe anlegen")
            .Produces<TaskDetail>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/{id:guid}", PatchTaskAsync)
            .WithSummary("Aufgabe ändern (JSON Merge Patch); completed: true erledigt, false öffnet wieder")
            .Accepts<JsonObject>(MergePatch.ContentType, "application/json")
            .Produces<TaskDetail>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    public static void MapStageEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/stages", GetStagesAsync)
            .WithTags("Phasen")
            .WithSummary("Phasen der Pipeline in ihrer Reihenfolge")
            .Produces<IReadOnlyList<StageResponse>>();
    }

    private static async Task<IResult> CreateActivityAsync(
        CreateActivityRequest request,
        ICommandHandler<LogActivity.Command, LogActivity.Result> logActivity,
        CancellationToken cancellationToken)
    {
        var result = await logActivity.Handle(
            new LogActivity.Command(
                request.Type ?? ActivityType.Note,
                request.Body,
                request.Subject,
                request.OccurredAt,
                request.ContactId,
                request.OrganizationId,
                request.OpportunityId),
            cancellationToken);

        return result.Match<IResult>(r => TypedResults.Created((string?)null, new CreatedResponse(r.Id)), ApiResults.Problem);
    }

    private static Task<IResult> GetTaskAsync(Guid id, IQueryHandler<GetTask.Query, GetTask.Result> getTask, CancellationToken cancellationToken) =>
        LoadTaskAsync(id, getTask, created: false, cancellationToken);

    private static async Task<IResult> CreateTaskAsync(
        CreateTaskRequest request,
        ICommandHandler<CreateTask.Command, CreateTask.Result> createTask,
        IQueryHandler<GetTask.Query, GetTask.Result> getTask,
        CancellationToken cancellationToken)
    {
        var result = await createTask.Handle(
            new CreateTask.Command(request.Title, request.DueDate, request.ContactId, request.OrganizationId, request.OpportunityId),
            cancellationToken);

        return result.IsSuccess
            ? await LoadTaskAsync(result.Value.Id, getTask, created: true, cancellationToken)
            : ApiResults.Problem(result.Error);
    }

    private static async Task<IResult> PatchTaskAsync(
        Guid id,
        JsonObject? body,
        IOptions<JsonOptions> json,
        IQueryHandler<GetTask.Query, GetTask.Result> getTask,
        ICommandHandler<UpdateTask.Command, UpdateTask.Result> updateTask,
        ICommandHandler<CompleteTask.Command, CompleteTask.Result> completeTask,
        ICommandHandler<ReopenTask.Command, ReopenTask.Result> reopenTask,
        CancellationToken cancellationToken)
    {
        var current = await getTask.Handle(new GetTask.Query(id), cancellationToken);
        if (current.IsFailure)
        {
            return ApiResults.Problem(current.Error);
        }

        var t = current.Value;
        var patch = MergePatch.Parse(body, TaskPatchFields, json.Value.SerializerOptions);
        var title = patch.Value("title", t.Title);
        var dueDate = patch.Value("dueDate", t.DueDate);
        var completed = patch.Value("completed", t.CompletedAt is not null);
        if (!patch.IsValid)
        {
            return patch.ValidationProblem();
        }

        if (patch.Has("title") || patch.Has("dueDate"))
        {
            var updated = await updateTask.Handle(new UpdateTask.Command(id, title, dueDate), cancellationToken);
            if (updated.IsFailure)
            {
                return ApiResults.Problem(updated.Error);
            }
        }

        if (completed != (t.CompletedAt is not null))
        {
            var changed = completed
                ? (await completeTask.Handle(new CompleteTask.Command(id), cancellationToken)).Match(_ => (Error?)null, e => e)
                : (await reopenTask.Handle(new ReopenTask.Command(id), cancellationToken)).Match(_ => (Error?)null, e => e);
            if (changed is not null)
            {
                return ApiResults.Problem(changed);
            }
        }

        return await LoadTaskAsync(id, getTask, created: false, cancellationToken);
    }

    private static async Task<IResult> LoadTaskAsync(
        Guid id, IQueryHandler<GetTask.Query, GetTask.Result> getTask, bool created, CancellationToken cancellationToken)
    {
        var task = await getTask.Handle(new GetTask.Query(id), cancellationToken);
        if (task.IsFailure)
        {
            return ApiResults.Problem(task.Error);
        }

        var t = task.Value;
        var detail = new TaskDetail(t.Id, t.Title, t.DueDate, t.CompletedAt is not null, t.CompletedAt, t.ContactId, t.OrganizationId, t.OpportunityId);
        return created ? TypedResults.Created($"{ApiEndpoints.BasePath}/tasks/{id}", detail) : TypedResults.Ok(detail);
    }

    private static async Task<IResult> GetStagesAsync(
        IQueryHandler<GetStages.Query, GetStages.Result> getStages, CancellationToken cancellationToken)
    {
        var result = await getStages.Handle(new GetStages.Query(), cancellationToken);
        return result.Match<IResult>(
            r => TypedResults.Ok<IReadOnlyList<StageResponse>>([.. r.Items.Select(s => new StageResponse(s.Id, s.Name, s.Status, s.SortOrder))]),
            ApiResults.Problem);
    }
}
