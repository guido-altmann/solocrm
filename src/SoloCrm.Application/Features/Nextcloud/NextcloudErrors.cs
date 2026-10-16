using SoloCrm.Application.Abstractions;

namespace SoloCrm.Application.Features.Nextcloud;

/// <summary>Business errors of the Nextcloud sync.</summary>
public static class NextcloudErrors
{
    public static readonly Error NotConnected =
        Error.Failure("Nextcloud.NotConnected", "Es ist keine Nextcloud verbunden.");

    public static readonly Error CredentialRejected =
        Error.Failure("Nextcloud.CredentialRejected", "Der Abgleich ist pausiert, weil Nextcloud die Anmeldung abgelehnt hat. Bitte die Zugangsdaten neu speichern.");

    public static readonly Error UnreadablePassword =
        Error.Failure("Nextcloud.UnreadablePassword", "Das gespeicherte App-Passwort ist nicht lesbar (Schlüssel gewechselt). Bitte die Zugangsdaten neu speichern.");
}
