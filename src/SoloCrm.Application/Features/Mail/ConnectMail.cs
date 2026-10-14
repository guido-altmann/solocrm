using FluentValidation;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Mail;

/// <summary>Completes the Microsoft sign-in and stores the connection (ADR-013).</summary>
public static class ConnectMail
{
    public sealed record Command(string? Code, string? CodeVerifier, string? RedirectUri);

    /// <param name="Account">The connected mailbox address.</param>
    public sealed record Result(string Account);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(c => c.Code).NotEmpty().WithMessage("Der Anmeldecode fehlt.");
            RuleFor(c => c.CodeVerifier).NotEmpty().WithMessage("Der Prüfwert der Anmeldung fehlt.");
            RuleFor(c => c.RedirectUri).NotEmpty().WithMessage("Die Rückkehr-Adresse fehlt.");
        }
    }

    public sealed class Handler(IMailConnector connector, IValidator<Command> validator) : ICommandHandler<Command, Result>
    {
        public async Task<Result<Result>> Handle(Command command, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(command, cancellationToken);
            if (!validation.IsValid)
            {
                return validation.ToValidationError();
            }

            if (!connector.IsConfigured)
            {
                return Error.Failure("Mail.NotConfigured", "Die Microsoft-365-Anbindung ist nicht eingerichtet.");
            }

            try
            {
                return new Result(await connector.ConnectAsync(command.Code!, command.CodeVerifier!, command.RedirectUri!, cancellationToken));
            }
            catch (MailSourceException ex)
            {
                return Error.Failure("Mail.ConnectFailed", ex.Message);
            }
        }
    }
}
