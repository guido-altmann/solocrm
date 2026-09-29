namespace SoloCrm.Domain.Common;

/// <summary>
/// Optional key/value fields stored as jsonb; precursor of custom properties (SPEC 2.2).
/// </summary>
public interface IHasExtraFields
{
    IReadOnlyDictionary<string, string> ExtraFields { get; }
}
