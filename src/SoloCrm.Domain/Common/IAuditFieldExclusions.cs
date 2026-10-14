namespace SoloCrm.Domain.Common;

/// <summary>
/// Lets an <see cref="IAuditable"/> entity keep individual fields out of the audit trail, e.g. imported content that
/// is already stored on the record itself and must not be duplicated (ADR-013).
/// </summary>
public interface IAuditFieldExclusions
{
    bool IsExcludedFromAudit(string fieldName);
}
