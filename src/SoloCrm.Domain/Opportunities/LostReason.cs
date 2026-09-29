namespace SoloCrm.Domain.Opportunities;

/// <summary>Required when an opportunity moves to a stage with status <see cref="StageStatus.Lost"/>.</summary>
public enum LostReason
{
    Price,
    Timing,
    OtherCandidate,
    ProjectCancelled,
    NoResponse,
    DeclinedByMe,
    Other,
}
