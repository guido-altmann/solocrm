using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Opportunities;
using SoloCrm.Application.Features.Tags;
using SoloCrm.Application.Features.Timeline;
using SoloCrm.Domain.Common;
using SoloCrm.Domain.Opportunities;

namespace SoloCrm.Web.Endpoints;

/// <summary><c>/api/v1/opportunities</c> (SPEC 5); <c>PATCH</c> with <c>stageId</c> changes the stage like the pipeline.</summary>
internal static class OpportunityEndpoints
{
    private static readonly string[] PatchFields =
    [
        "title", "stageId", "lostReason", "clientOrganizationId", "agencyOrganizationId", "primaryContactId", "pricingModel",
        "amount", "currency", "startDate", "durationValue", "durationUnit", "utilization", "remotePercentage", "source",
        "receivedOn",
    ];

    public static void MapOpportunityEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/opportunities").WithTags("Anfragen");

        group.MapGet("/", ListAsync)
            .WithSummary("Anfragen auflisten (optional je Phase)")
            .Produces<PageResponse<OpportunityListItem>>()
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Anfrage lesen")
            .Produces<OpportunityDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithSummary("Anfrage anlegen")
            .Produces<OpportunityDetail>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/{id:guid}", PatchAsync)
            .WithSummary("Anfrage ändern (JSON Merge Patch); stageId wechselt die Phase, „Verloren“ verlangt lostReason")
            .Accepts<JsonObject>(MergePatch.ContentType, "application/json")
            .Produces<OpportunityDetail>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> ListAsync(
        string? search,
        string[]? tag,
        Guid? stageId,
        int? page,
        int? pageSize,
        IQueryHandler<GetOpportunities.Query, GetOpportunities.Result> getOpportunities,
        IQueryHandler<GetTags.Query, GetTags.Result> getTags,
        CancellationToken cancellationToken)
    {
        if (ApiQuery.ValidatePaging(page, pageSize, out var pageIndex, out var size) is { } invalid)
        {
            return invalid;
        }

        var (tagIds, matchesNothing) = await ApiQuery.ResolveTagsAsync(tag, getTags, cancellationToken);
        if (matchesNothing)
        {
            return TypedResults.Ok(ApiQuery.EmptyPage<OpportunityListItem>(pageIndex, size));
        }

        var result = await getOpportunities.Handle(
            new GetOpportunities.Query(search, stageId, PageIndex: pageIndex, PageSize: size, TagIds: tagIds), cancellationToken);
        return result.Match<IResult>(
            r => TypedResults.Ok(new PageResponse<OpportunityListItem>(
                [.. r.Items.Select(o => new OpportunityListItem(
                    o.Id, o.Title, o.StageId, o.StageName, o.StageStatus, o.ClientOrganizationId, o.AgencyOrganizationId,
                    o.PrimaryContactId, o.Pricing?.Model, o.Pricing?.Amount, o.Pricing?.Currency, o.Source, o.IsArchived,
                    o.ReceivedOn, o.CreatedAt, TagResponse.From(o.Tags)))],
                pageIndex + 1,
                size,
                r.TotalCount)),
            ApiResults.Problem);
    }

    private static Task<IResult> GetAsync(
        Guid id,
        IQueryHandler<GetOpportunity.Query, GetOpportunity.Result> getOpportunity,
        IQueryHandler<GetRecordTags.Query, GetRecordTags.Result> getRecordTags,
        CancellationToken cancellationToken) =>
        LoadAsync(id, getOpportunity, getRecordTags, created: false, cancellationToken);

    private static async Task<IResult> CreateAsync(
        CreateOpportunityRequest request,
        ICommandHandler<CreateOpportunity.Command, CreateOpportunity.Result> createOpportunity,
        IQueryHandler<GetOpportunity.Query, GetOpportunity.Result> getOpportunity,
        IQueryHandler<GetRecordTags.Query, GetRecordTags.Result> getRecordTags,
        CancellationToken cancellationToken)
    {
        var result = await createOpportunity.Handle(
            new CreateOpportunity.Command(
                request.Title,
                request.StageId,
                request.ClientOrganizationId,
                request.AgencyOrganizationId,
                request.PrimaryContactId,
                request.PricingModel,
                request.Amount,
                request.Currency,
                request.StartDate,
                request.DurationValue,
                request.DurationUnit,
                request.Utilization,
                request.RemotePercentage,
                request.Source,
                request.ReceivedOn),
            cancellationToken);

        return result.IsSuccess
            ? await LoadAsync(result.Value.Id, getOpportunity, getRecordTags, created: true, cancellationToken)
            : ApiResults.Problem(result.Error);
    }

    /// <summary>Loads the current state, overwrites the sent fields and calls <see cref="UpdateOpportunity"/> (decision 4).</summary>
    private static async Task<IResult> PatchAsync(
        Guid id,
        JsonObject? body,
        IOptions<JsonOptions> json,
        IQueryHandler<GetOpportunity.Query, GetOpportunity.Result> getOpportunity,
        ICommandHandler<UpdateOpportunity.Command, UpdateOpportunity.Result> updateOpportunity,
        IQueryHandler<GetRecordTags.Query, GetRecordTags.Result> getRecordTags,
        CancellationToken cancellationToken)
    {
        var current = await getOpportunity.Handle(new GetOpportunity.Query(id), cancellationToken);
        if (current.IsFailure)
        {
            return ApiResults.Problem(current.Error);
        }

        var o = current.Value;
        var patch = MergePatch.Parse(body, PatchFields, json.Value.SerializerOptions);
        var command = new UpdateOpportunity.Command(
            id,
            patch.Value("title", o.Title),
            patch.Value("stageId", o.StageId),
            patch.Value("lostReason", o.LostReason),
            patch.Value("clientOrganizationId", o.ClientOrganizationId),
            patch.Value("agencyOrganizationId", o.AgencyOrganizationId),
            patch.Value("primaryContactId", o.PrimaryContactId),
            patch.Value("pricingModel", o.Pricing?.Model),
            patch.Value("amount", o.Pricing?.Amount),
            patch.Value("currency", o.Pricing?.Currency),
            patch.Value("startDate", o.StartDate),
            patch.Value("durationValue", o.Duration?.Value),
            patch.Value("durationUnit", o.Duration?.Unit),
            patch.Value("utilization", o.Utilization),
            patch.Value("remotePercentage", o.RemotePercentage),
            patch.Value<LeadSource?>("source", o.Source),
            patch.Value<DateOnly?>("receivedOn", o.ReceivedOn));
        if (!patch.IsValid)
        {
            return patch.ValidationProblem();
        }

        // The date is required, so null cannot clear it.
        if (command.ReceivedOn is null)
        {
            return ApiResults.ValidationProblem(new Dictionary<string, string[]> { ["ReceivedOn"] = ["Bitte ein Eingangsdatum angeben."] });
        }

        var result = await updateOpportunity.Handle(command, cancellationToken);
        return result.IsSuccess
            ? await LoadAsync(id, getOpportunity, getRecordTags, created: false, cancellationToken)
            : ApiResults.Problem(result.Error);
    }

    private static async Task<IResult> LoadAsync(
        Guid id,
        IQueryHandler<GetOpportunity.Query, GetOpportunity.Result> getOpportunity,
        IQueryHandler<GetRecordTags.Query, GetRecordTags.Result> getRecordTags,
        bool created,
        CancellationToken cancellationToken)
    {
        var opportunity = await getOpportunity.Handle(new GetOpportunity.Query(id), cancellationToken);
        if (opportunity.IsFailure)
        {
            return ApiResults.Problem(opportunity.Error);
        }

        var tags = await getRecordTags.Handle(new GetRecordTags.Query(TimelineRecordType.Opportunity, id), cancellationToken);
        var o = opportunity.Value;
        var detail = new OpportunityDetail(
            o.Id, o.Title, o.StageId, o.StageName, o.StageStatus, o.LostReason, o.ClosedAt, o.ClientOrganizationId,
            o.AgencyOrganizationId, o.PrimaryContactId, o.Pricing?.Model, o.Pricing?.Amount, o.Pricing?.Currency, o.StartDate,
            o.Duration?.Value, o.Duration?.Unit, o.Utilization, o.RemotePercentage, o.Source, o.ReceivedOn, o.IsArchived, o.EstimatedValue,
            o.MonthlyRecurringValue, tags.IsSuccess ? TagResponse.From(tags.Value.Items) : []);

        return created ? TypedResults.Created($"{ApiEndpoints.BasePath}/opportunities/{id}", detail) : TypedResults.Ok(detail);
    }
}
