using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Contacts;
using SoloCrm.Domain.Opportunities;
using SoloCrm.Domain.Organizations;

namespace SoloCrm.Application.Features.Search;

/// <summary>
/// Filters and relevance of the search (ADR-007), shared by the command palette, the lists and the autocompletes.
/// A record matches if every word is a prefix of a word in its search vector (full text) or if the input is
/// similar to its name (trigram word similarity, operator <c>&lt;%</c>, threshold <c>pg_trgm.word_similarity_threshold</c>
/// = 0.6). The relevance adds the word similarity (0–1) and <c>ts_rank</c>, so exact and prefix hits rank above typos.
/// </summary>
public static class SearchPredicates
{
    /// <summary>Must match the configuration of the generated columns (<c>SearchConfiguration</c> in Infrastructure).</summary>
    private const string Config = "simple";

    /// <summary>
    /// A contact's organization („Firma“) counts only with half weight and only if it matches better than the contact's
    /// own name (<c>GREATEST</c>), so that a similar organization name does not push aside a better name match.
    /// </summary>
    private const double OrganizationWeight = 0.5;

    /// <summary>
    /// Ids of all organizations matching <paramref name="term"/>, including archived ones: the employer of a contact
    /// stays its „Firma“. Resolved first, so that <see cref="ContactMatches"/> can use the indexes of the contacts
    /// (<c>BitmapOr</c>); an <c>OR</c> over the joined organization would force a sequential scan.
    /// </summary>
    public static Task<List<Guid>> MatchingOrganizationIdsAsync(this ICrmDbContext db, SearchTerm term, CancellationToken cancellationToken) =>
        db.Organizations.AsNoTracking()
            .Where(OrganizationMatches(term))
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);

    /// <param name="organizationIds">From <see cref="MatchingOrganizationIdsAsync"/>.</param>
    public static Expression<Func<Contact, bool>> ContactMatches(SearchTerm term, IReadOnlyCollection<Guid> organizationIds)
    {
        var (text, tsQuery) = (term.Text, term.TsQuery);
        return c =>
            EF.Property<NpgsqlTsVector>(c, SearchColumns.Vector).Matches(EF.Functions.ToTsQuery(Config, tsQuery))
            || EF.Functions.TrigramsAreWordSimilar(text, EF.Property<string>(c, SearchColumns.Name))
            || (c.OrganizationId != null && organizationIds.Contains(c.OrganizationId.Value));
    }

    public static Expression<Func<Contact, double>> ContactRelevance(SearchTerm term)
    {
        var (text, tsQuery) = (term.Text, term.TsQuery);
        return c => Math.Max(
            EF.Functions.TrigramsWordSimilarity(text, EF.Property<string>(c, SearchColumns.Name))
                + EF.Property<NpgsqlTsVector>(c, SearchColumns.Vector).Rank(EF.Functions.ToTsQuery(Config, tsQuery)),
            OrganizationWeight * EF.Functions.TrigramsWordSimilarity(text, c.Organization!.Name ?? ""));
    }

    public static Expression<Func<Organization, bool>> OrganizationMatches(SearchTerm term)
    {
        var (text, tsQuery) = (term.Text, term.TsQuery);
        return o =>
            EF.Property<NpgsqlTsVector>(o, SearchColumns.Vector).Matches(EF.Functions.ToTsQuery(Config, tsQuery))
            || EF.Functions.TrigramsAreWordSimilar(text, o.Name);
    }

    public static Expression<Func<Organization, double>> OrganizationRelevance(SearchTerm term)
    {
        var (text, tsQuery) = (term.Text, term.TsQuery);
        return o =>
            EF.Functions.TrigramsWordSimilarity(text, o.Name)
            + EF.Property<NpgsqlTsVector>(o, SearchColumns.Vector).Rank(EF.Functions.ToTsQuery(Config, tsQuery));
    }

    public static Expression<Func<Opportunity, bool>> OpportunityMatches(SearchTerm term)
    {
        var (text, tsQuery) = (term.Text, term.TsQuery);
        return o =>
            EF.Property<NpgsqlTsVector>(o, SearchColumns.Vector).Matches(EF.Functions.ToTsQuery(Config, tsQuery))
            || EF.Functions.TrigramsAreWordSimilar(text, o.Title);
    }

    public static Expression<Func<Opportunity, double>> OpportunityRelevance(SearchTerm term)
    {
        var (text, tsQuery) = (term.Text, term.TsQuery);
        return o =>
            EF.Functions.TrigramsWordSimilarity(text, o.Title)
            + EF.Property<NpgsqlTsVector>(o, SearchColumns.Vector).Rank(EF.Functions.ToTsQuery(Config, tsQuery));
    }
}
