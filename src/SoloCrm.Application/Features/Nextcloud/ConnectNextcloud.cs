using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SoloCrm.Application.Abstractions;
using SoloCrm.Domain.Nextcloud;

namespace SoloCrm.Application.Features.Nextcloud;

/// <summary>
/// Saves the Nextcloud credentials (ADR-014). The connection is checked first: SoloCRM signs in, finds the address
/// book „SoloCRM“ or creates it. Saving again resumes a sync paused after a rejected password; switching to another
/// account (or a new address book) forgets what was mirrored before.
/// </summary>
public static class ConnectNextcloud
{
    public sealed record Command(string? ServerUrl, string? UserName, string? AppPassword);

    public sealed record Result(string ServerUrl, string UserName);

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator(NextcloudServers servers)
        {
            RuleFor(c => c.ServerUrl)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .WithMessage("Bitte die Adresse der Nextcloud angeben.")
                .MaximumLength(NextcloudConnection.ServerUrlMaxLength)
                .WithMessage($"Die Adresse darf höchstens {NextcloudConnection.ServerUrlMaxLength} Zeichen lang sein.")
                .Must(servers.IsAllowed)
                .WithMessage("Bitte eine vollständige https-Adresse angeben, z. B. https://cloud.example.com.");
            RuleFor(c => c.UserName)
                .NotEmpty()
                .WithMessage("Bitte den Benutzernamen angeben.")
                .MaximumLength(NextcloudConnection.UserNameMaxLength)
                .WithMessage($"Der Benutzername darf höchstens {NextcloudConnection.UserNameMaxLength} Zeichen lang sein.");
            RuleFor(c => c.AppPassword)
                .NotEmpty()
                .WithMessage("Bitte ein App-Passwort angeben.");
        }
    }

    public sealed class Handler(
        ICrmDbContextFactory dbFactory,
        IDavSyncTarget target,
        ISecretProtector protector,
        IValidator<Command> validator,
        TimeProvider timeProvider) : ICommandHandler<Command, Result>
    {
        public async Task<Result<Result>> Handle(Command command, CancellationToken cancellationToken)
        {
            var validation = await validator.ValidateAsync(command, cancellationToken);
            if (!validation.IsValid)
            {
                return validation.ToValidationError();
            }

            var serverUrl = NextcloudConnection.NormalizeServerUrl(command.ServerUrl!);
            var userName = command.UserName!.Trim();
            var account = new DavAccount(new Uri(serverUrl), userName, command.AppPassword!);

            string addressBook;
            try
            {
                addressBook = await target.EnsureAddressBookAsync(account, cancellationToken);
            }
            catch (DavException ex)
            {
                return Error.Failure("Nextcloud.ConnectFailed", ex.Message);
            }

            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var connection = await db.NextcloudConnections.SingleOrDefaultAsync(cancellationToken);
            var protectedPassword = protector.Protect(command.AppPassword!);
            var now = timeProvider.GetUtcNow();
            if (connection is null)
            {
                connection = new NextcloudConnection(serverUrl, userName, protectedPassword, now);
                db.NextcloudConnections.Add(connection);
            }
            else
            {
                if (!connection.IsSameAccount(serverUrl, userName) || connection.AddressBookHref != addressBook)
                {
                    // The mapping points into another address book; the next sync writes everything anew.
                    await db.NextcloudSyncItems.Where(i => i.EntityType == NextcloudItemType.Contact).ExecuteDeleteAsync(cancellationToken);
                }

                connection.Reconnect(serverUrl, userName, protectedPassword, now);
            }

            connection.UseAddressBook(addressBook);
            await db.SaveChangesAsync(cancellationToken);
            return new Result(connection.ServerUrl, connection.UserName);
        }
    }
}
