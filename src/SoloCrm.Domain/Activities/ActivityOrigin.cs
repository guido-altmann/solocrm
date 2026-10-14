namespace SoloCrm.Domain.Activities;

/// <summary>Where an activity comes from (ADR-013).</summary>
public enum ActivityOrigin
{
    /// <summary>Entered by the user.</summary>
    Manual,

    /// <summary>Imported from the connected mailbox; subject and body are kept out of the audit trail.</summary>
    Mail,
}
