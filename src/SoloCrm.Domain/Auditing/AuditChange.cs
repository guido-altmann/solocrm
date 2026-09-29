namespace SoloCrm.Domain.Auditing;

/// <summary>
/// Old and new value of one field, formatted invariantly. Complex type members use dotted names (<c>Pricing.Amount</c>).
/// </summary>
public sealed record AuditChange(string Field, string? Old, string? New);
