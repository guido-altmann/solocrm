using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Mail;

/// <summary>Removes the stored token and the sync progress; imported activities stay (ADR-013).</summary>
public static class DisconnectMail
{
    public sealed record Command;

    public sealed record Result;

    public sealed class Handler(IMailConnector connector) : ICommandHandler<Command, Result>
    {
        public async Task<Result<Result>> Handle(Command command, CancellationToken cancellationToken)
        {
            await connector.DisconnectAsync(cancellationToken);
            return new Result();
        }
    }
}
