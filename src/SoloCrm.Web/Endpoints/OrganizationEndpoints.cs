using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Application.Features.Tags;
using SoloCrm.Application.Features.Timeline;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Web.Endpoints;

/// <summary><c>/api/v1/organizations</c> (SPEC 5).</summary>
internal static class OrganizationEndpoints
{
    private static readonly string[] PatchFields = ["name", "type", "website", "city", "notes"];

    public static void MapOrganizationEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/organizations").WithTags("Organisationen");

        group.MapGet("/", ListAsync)
            .WithSummary("Organisationen auflisten")
            .Produces<PageResponse<OrganizationListItem>>()
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Organisation lesen")
            .Produces<OrganizationDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithSummary("Organisation anlegen")
            .Produces<OrganizationDetail>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        group.MapPatch("/{id:guid}", PatchAsync)
            .WithSummary("Organisation ändern (JSON Merge Patch)")
            .Accepts<JsonObject>(MergePatch.ContentType, "application/json")
            .Produces<OrganizationDetail>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListAsync(
        string? search,
        string[]? tag,
        int? page,
        int? pageSize,
        IQueryHandler<GetOrganizations.Query, GetOrganizations.Result> getOrganizations,
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
            return TypedResults.Ok(ApiQuery.EmptyPage<OrganizationListItem>(pageIndex, size));
        }

        var result = await getOrganizations.Handle(
            new GetOrganizations.Query(Search: search, PageIndex: pageIndex, PageSize: size, TagIds: tagIds), cancellationToken);
        return result.Match<IResult>(
            r => TypedResults.Ok(new PageResponse<OrganizationListItem>(
                [.. r.Items.Select(o => new OrganizationListItem(
                    o.Id, o.Name, o.Type, o.Website, o.City, o.IsArchived, o.CreatedAt, TagResponse.From(o.Tags)))],
                pageIndex + 1,
                size,
                r.TotalCount)),
            ApiResults.Problem);
    }

    private static Task<IResult> GetAsync(
        Guid id,
        IQueryHandler<GetOrganization.Query, GetOrganization.Result> getOrganization,
        IQueryHandler<GetRecordTags.Query, GetRecordTags.Result> getRecordTags,
        CancellationToken cancellationToken) =>
        LoadAsync(id, getOrganization, getRecordTags, created: false, cancellationToken);

    private static async Task<IResult> CreateAsync(
        CreateOrganizationRequest request,
        ICommandHandler<CreateOrganization.Command, CreateOrganization.Result> createOrganization,
        IQueryHandler<GetOrganization.Query, GetOrganization.Result> getOrganization,
        IQueryHandler<GetRecordTags.Query, GetRecordTags.Result> getRecordTags,
        CancellationToken cancellationToken)
    {
        var result = await createOrganization.Handle(
            new CreateOrganization.Command(request.Name, request.Type ?? OrganizationType.Other, request.Website, request.City, request.Notes),
            cancellationToken);

        return result.IsSuccess
            ? await LoadAsync(result.Value.Id, getOrganization, getRecordTags, created: true, cancellationToken)
            : ApiResults.Problem(result.Error);
    }

    private static async Task<IResult> PatchAsync(
        Guid id,
        JsonObject? body,
        IOptions<JsonOptions> json,
        IQueryHandler<GetOrganization.Query, GetOrganization.Result> getOrganization,
        ICommandHandler<UpdateOrganization.Command, UpdateOrganization.Result> updateOrganization,
        IQueryHandler<GetRecordTags.Query, GetRecordTags.Result> getRecordTags,
        CancellationToken cancellationToken)
    {
        var current = await getOrganization.Handle(new GetOrganization.Query(id), cancellationToken);
        if (current.IsFailure)
        {
            return ApiResults.Problem(current.Error);
        }

        var o = current.Value;
        var patch = MergePatch.Parse(body, PatchFields, json.Value.SerializerOptions);
        var command = new UpdateOrganization.Command(
            id,
            patch.Value("name", o.Name),
            patch.Value("type", o.Type),
            patch.Value("website", o.Website),
            patch.Value("city", o.City),
            patch.Value("notes", o.Notes));
        if (!patch.IsValid)
        {
            return patch.ValidationProblem();
        }

        var result = await updateOrganization.Handle(command, cancellationToken);
        return result.IsSuccess
            ? await LoadAsync(id, getOrganization, getRecordTags, created: false, cancellationToken)
            : ApiResults.Problem(result.Error);
    }

    private static async Task<IResult> LoadAsync(
        Guid id,
        IQueryHandler<GetOrganization.Query, GetOrganization.Result> getOrganization,
        IQueryHandler<GetRecordTags.Query, GetRecordTags.Result> getRecordTags,
        bool created,
        CancellationToken cancellationToken)
    {
        var organization = await getOrganization.Handle(new GetOrganization.Query(id), cancellationToken);
        if (organization.IsFailure)
        {
            return ApiResults.Problem(organization.Error);
        }

        var tags = await getRecordTags.Handle(new GetRecordTags.Query(TimelineRecordType.Organization, id), cancellationToken);
        var o = organization.Value;
        var detail = new OrganizationDetail(
            o.Id, o.Name, o.Type, o.Website, o.City, o.Notes, o.IsArchived, tags.IsSuccess ? TagResponse.From(tags.Value.Items) : []);

        return created ? TypedResults.Created($"{ApiEndpoints.BasePath}/organizations/{id}", detail) : TypedResults.Ok(detail);
    }
}
