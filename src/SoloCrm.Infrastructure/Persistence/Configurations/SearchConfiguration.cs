using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;
using SoloCrm.Application.Features.Search;

namespace SoloCrm.Infrastructure.Persistence.Configurations;

/// <summary>
/// Generated search columns and their indexes (ADR-007). Stored generated columns are recomputed by PostgreSQL
/// on every insert and update; EF only reads them.
/// </summary>
internal static class SearchConfiguration
{
    /// <summary>Full-text configuration without stemming and stop words: the searched texts are mostly proper names.</summary>
    public const string TextSearchConfig = "simple";

    /// <summary>
    /// Adds the generated <c>tsvector</c> column with a GIN index. <paramref name="parts"/> are SQL expressions over
    /// the table columns with their weight (A = name/title, B = further fields).
    /// </summary>
    public static void HasSearchVector<T>(this EntityTypeBuilder<T> builder, params (string Sql, char Weight)[] parts)
        where T : class
    {
        var sql = string.Join(
            " || ",
            parts.Select(p => $"setweight(to_tsvector('{TextSearchConfig}', coalesce({p.Sql}, '')), '{p.Weight}')"));

        builder.Property<NpgsqlTsVector>(SearchColumns.Vector).HasComputedColumnSql(sql, stored: true);
        builder.HasIndex(SearchColumns.Vector).HasMethod("gin");
    }

    /// <summary>
    /// Adds a <c>pg_trgm</c> GIN index for the word similarity operator (<c>&lt;%</c>) on a text column. The index is
    /// named explicitly because the column may already have a B-tree index for sorting.
    /// </summary>
    public static void HasTrigramIndex<T>(this EntityTypeBuilder<T> builder, string propertyName, string indexName)
        where T : class =>
        builder.HasIndex([propertyName], indexName)
            .HasDatabaseName(indexName)
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops");

    /// <summary>
    /// Splits e-mail addresses and URLs into words, so that „acme“ finds „https://www.acme.de“. The default parser
    /// would keep them as single email/host tokens.
    /// </summary>
    public static string SplitIntoWords(string column) => $"regexp_replace({column}, '[@./:?#=&_+-]+', ' ', 'g')";
}
