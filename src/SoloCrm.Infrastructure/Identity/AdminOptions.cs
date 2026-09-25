namespace SoloCrm.Infrastructure.Identity;

/// <summary>
/// Initial admin account, bound from the "Admin" section (env: <c>Admin__Email</c>, <c>Admin__InitialPassword</c>).
/// Only used while no user exists yet.
/// </summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    public string? Email { get; init; }

    public string? InitialPassword { get; init; }
}
