using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.Tags;

namespace SoloCrm.Web.Endpoints;

/// <summary>Shared list parameters of the REST API (SPEC 5): <c>search</c>, <c>tag</c>, <c>page</c> (1-based), <c>pageSize</c>.</summary>
internal static class ApiQuery
{
    /// <summary>Checks <c>page</c> and <c>pageSize</c>; returns a validation problem or <c>null</c>.</summary>
    public static IResult? ValidatePaging(int? page, int? pageSize, out int pageIndex, out int size)
    {
        pageIndex = (page ?? 1) - 1;
        size = pageSize ?? Paging.DefaultPageSize;

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (page < 1)
        {
            errors["page"] = ["Die Seite beginnt bei 1."];
        }

        if (size is < 1 or > Paging.MaxPageSize)
        {
            errors["pageSize"] = [$"Die Seitengröße muss zwischen 1 und {Paging.MaxPageSize} liegen."];
        }

        return errors.Count == 0 ? null : ApiResults.ValidationProblem(errors);
    }

    /// <summary>
    /// Resolves <c>tag</c> values (names regardless of case, or ids) to tag ids. Several tags are OR-combined like in the
    /// UI (US-04 AK2); if none of them exists, the list is empty (<c>MatchesNothing</c>).
    /// </summary>
    public static async Task<(IReadOnlyCollection<Guid>? Ids, bool MatchesNothing)> ResolveTagsAsync(
        string[]? tags,
        IQueryHandler<GetTags.Query, GetTags.Result> getTags,
        CancellationToken cancellationToken)
    {
        var requested = tags?.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList() ?? [];
        if (requested.Count == 0)
        {
            return (null, false);
        }

        var all = await getTags.Handle(new GetTags.Query(), cancellationToken);
        var ids = all.IsFailure
            ? []
            : all.Value.Items
                .Where(t => requested.Exists(r => string.Equals(r, t.Name, StringComparison.OrdinalIgnoreCase)
                    || (Guid.TryParse(r, out var id) && id == t.Id)))
                .Select(t => t.Id)
                .ToList();

        return ids.Count == 0 ? (null, true) : (ids, false);
    }

    public static PageResponse<T> EmptyPage<T>(int pageIndex, int pageSize) => new([], pageIndex + 1, pageSize, 0);
}
