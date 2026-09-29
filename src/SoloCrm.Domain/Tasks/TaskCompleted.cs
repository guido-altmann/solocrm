using SoloCrm.Domain.Common;

namespace SoloCrm.Domain.Tasks;

public sealed record TaskCompleted(Guid TaskId, DateTimeOffset CompletedAt) : IDomainEvent;
