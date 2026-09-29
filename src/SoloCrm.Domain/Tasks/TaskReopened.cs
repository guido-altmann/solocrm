using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.Tasks;

/// <summary>Raised when a completed task is opened again, e.g. by the undo after completing it (SPEC 2.6).</summary>
public sealed record TaskReopened(Guid TaskId) : IDomainEvent;
