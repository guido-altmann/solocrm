using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Contacts;
using SoloCrm.Application.Features.Organizations;
using SoloCrm.Application.Features.Tags;
using SoloCrm.Application.Features.Timeline;
using SoloCrm.Domain.Common;

namespace SoloCrm.Web.Endpoints;

/// <summary><c>/api/v1/contacts</c> (SPEC 5); typical n8n lead intake via <c>POST</c>.</summary>
internal static class ContactEndpoints
{
    private static readonly string[] PatchFields =
        ["firstName", "lastName", "email", "phone", "jobTitle", "linkedInUrl", "organizationId", "source"];

    public static void MapContactEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/contacts").WithTags("Kontakte");

        group.MapGet("/", ListAsync)
            .WithSummary("Kontakte auflisten (Suche wie in der Oberfläche, Tags ODER-verknüpft)")
            .Produces<PageResponse<ContactListItem>>()
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Kontakt lesen")
            .Produces<ContactDetail>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithSummary("Kontakt anlegen")
            .Produces<ContactDetail>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{id:guid}", PatchAsync)
            .WithSummary("Kontakt ändern (JSON Merge Patch: fehlendes Feld bleibt, null leert)")
            .Accepts<JsonObject>(MergePatch.ContentType, "application/json")
            .Produces<ContactDetail>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> ListAsync(
        string? search,
        string[]? tag,
        int? page,
        int? pageSize,
        IQueryHandler<GetContacts.Query, GetContacts.Result> getContacts,
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
            return TypedResults.Ok(ApiQuery.EmptyPage<ContactListItem>(pageIndex, size));
        }

        var result = await getContacts.Handle(
            new GetContacts.Query(Search: search, PageIndex: pageIndex, PageSize: size, TagIds: tagIds), cancellationToken);
        return result.Match<IResult>(
            r => TypedResults.Ok(new PageResponse<ContactListItem>(
                [.. r.Items.Select(c => new ContactListItem(
                    c.Id, c.FirstName, c.LastName, c.Email, c.Phone, c.JobTitle, c.OrganizationId, c.OrganizationName,
                    c.Source, c.IsArchived, c.CreatedAt, TagResponse.From(c.Tags)))],
                pageIndex + 1,
                size,
                r.TotalCount)),
            ApiResults.Problem);
    }

    private static Task<IResult> GetAsync(
        Guid id,
        IQueryHandler<GetContact.Query, GetContact.Result> getContact,
        IQueryHandler<GetRecordTags.Query, GetRecordTags.Result> getRecordTags,
        CancellationToken cancellationToken) =>
        LoadAsync(id, getContact, getRecordTags, created: false, cancellationToken);

    private static async Task<IResult> CreateAsync(
        CreateContactRequest request,
        ICommandHandler<CreateContact.Command, CreateContact.Result> createContact,
        IQueryHandler<FindOrganizationByName.Query, FindOrganizationByName.Result> findOrganization,
        IQueryHandler<GetContact.Query, GetContact.Result> getContact,
        IQueryHandler<GetRecordTags.Query, GetRecordTags.Result> getRecordTags,
        CancellationToken cancellationToken)
    {
        var organizationId = request.OrganizationId;
        string? newOrganizationName = null;
        if (organizationId is null && !string.IsNullOrWhiteSpace(request.OrganizationName))
        {
            var existing = await findOrganization.Handle(new FindOrganizationByName.Query(request.OrganizationName), cancellationToken);
            if (existing.IsFailure)
            {
                return ApiResults.Problem(existing.Error);
            }

            organizationId = existing.Value.Id;
            newOrganizationName = organizationId is null ? request.OrganizationName : null;
        }

        var result = await createContact.Handle(
            new CreateContact.Command(
                request.FirstName,
                request.LastName,
                request.Email,
                request.Phone,
                request.JobTitle,
                request.LinkedInUrl,
                organizationId,
                newOrganizationName,
                request.Source),
            cancellationToken);

        return result.IsSuccess
            ? await LoadAsync(result.Value.Id, getContact, getRecordTags, created: true, cancellationToken)
            : ApiResults.Problem(result.Error);
    }

    private static async Task<IResult> PatchAsync(
        Guid id,
        JsonObject? body,
        IOptions<JsonOptions> json,
        IQueryHandler<GetContact.Query, GetContact.Result> getContact,
        ICommandHandler<UpdateContact.Command, UpdateContact.Result> updateContact,
        IQueryHandler<GetRecordTags.Query, GetRecordTags.Result> getRecordTags,
        CancellationToken cancellationToken)
    {
        var current = await getContact.Handle(new GetContact.Query(id), cancellationToken);
        if (current.IsFailure)
        {
            return ApiResults.Problem(current.Error);
        }

        var c = current.Value;
        var patch = MergePatch.Parse(body, PatchFields, json.Value.SerializerOptions);
        var command = new UpdateContact.Command(
            id,
            patch.Value("firstName", c.FirstName),
            patch.Value("lastName", c.LastName),
            patch.Value("email", c.Email),
            patch.Value("phone", c.Phone),
            patch.Value("jobTitle", c.JobTitle),
            patch.Value("linkedInUrl", c.LinkedInUrl),
            patch.Value("organizationId", c.OrganizationId),
            null,
            patch.Value<LeadSource?>("source", c.Source));
        if (!patch.IsValid)
        {
            return patch.ValidationProblem();
        }

        var result = await updateContact.Handle(command, cancellationToken);
        return result.IsSuccess
            ? await LoadAsync(id, getContact, getRecordTags, created: false, cancellationToken)
            : ApiResults.Problem(result.Error);
    }

    private static async Task<IResult> LoadAsync(
        Guid id,
        IQueryHandler<GetContact.Query, GetContact.Result> getContact,
        IQueryHandler<GetRecordTags.Query, GetRecordTags.Result> getRecordTags,
        bool created,
        CancellationToken cancellationToken)
    {
        var contact = await getContact.Handle(new GetContact.Query(id), cancellationToken);
        if (contact.IsFailure)
        {
            return ApiResults.Problem(contact.Error);
        }

        var tags = await getRecordTags.Handle(new GetRecordTags.Query(TimelineRecordType.Contact, id), cancellationToken);
        var c = contact.Value;
        var detail = new ContactDetail(
            c.Id, c.FirstName, c.LastName, c.Email, c.Phone, c.JobTitle, c.LinkedInUrl, c.OrganizationId, c.OrganizationName,
            c.Source, c.IsArchived, tags.IsSuccess ? TagResponse.From(tags.Value.Items) : []);

        return created ? TypedResults.Created($"{ApiEndpoints.BasePath}/contacts/{id}", detail) : TypedResults.Ok(detail);
    }
}
