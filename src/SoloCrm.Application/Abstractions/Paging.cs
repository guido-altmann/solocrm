using FluentValidation;

namespace SoloCrm.Application.Abstractions;

public static class Paging
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
}

public interface IPagedQuery
{
    /// <summary>Zero-based page index.</summary>
    int PageIndex { get; }

    int PageSize { get; }
}

/// <summary>Shared rules for paged queries; include them via <c>Include(new PagingValidator&lt;T&gt;())</c>.</summary>
public sealed class PagingValidator<T> : AbstractValidator<T>
    where T : IPagedQuery
{
    public PagingValidator()
    {
        RuleFor(q => q.PageIndex)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Die Seitenzahl darf nicht negativ sein.");

        RuleFor(q => q.PageSize)
            .InclusiveBetween(1, Paging.MaxPageSize)
            .WithMessage($"Die Seitengröße muss zwischen 1 und {Paging.MaxPageSize} liegen.");
    }
}

public static class PagingExtensions
{
    public static IQueryable<T> Page<T>(this IQueryable<T> source, IPagedQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return source.Skip(query.PageIndex * query.PageSize).Take(query.PageSize);
    }
}
