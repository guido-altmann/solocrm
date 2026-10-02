using System.Globalization;
using Microsoft.AspNetCore.Identity;

namespace SoloCrm.Web.Components.Account;

/// <summary>German texts for the Identity errors shown on the account pages (UI texts are German).</summary>
internal sealed class GermanIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DefaultError() =>
        new() { Code = nameof(DefaultError), Description = "Ein unbekannter Fehler ist aufgetreten." };

    public override IdentityError PasswordMismatch() =>
        new() { Code = nameof(PasswordMismatch), Description = "Das aktuelle Passwort ist falsch." };

    public override IdentityError InvalidToken() =>
        new() { Code = nameof(InvalidToken), Description = "Der Code ist ungültig." };

    public override IdentityError PasswordTooShort(int length) => new()
    {
        Code = nameof(PasswordTooShort),
        Description = string.Create(CultureInfo.InvariantCulture, $"Das Passwort muss mindestens {length} Zeichen lang sein."),
    };

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => new()
    {
        Code = nameof(PasswordRequiresUniqueChars),
        Description = string.Create(CultureInfo.InvariantCulture, $"Das Passwort muss mindestens {uniqueChars} verschiedene Zeichen enthalten."),
    };

    public override IdentityError PasswordRequiresNonAlphanumeric() =>
        new() { Code = nameof(PasswordRequiresNonAlphanumeric), Description = "Das Passwort muss ein Sonderzeichen enthalten." };

    public override IdentityError PasswordRequiresDigit() =>
        new() { Code = nameof(PasswordRequiresDigit), Description = "Das Passwort muss eine Ziffer enthalten." };

    public override IdentityError PasswordRequiresLower() =>
        new() { Code = nameof(PasswordRequiresLower), Description = "Das Passwort muss einen Kleinbuchstaben enthalten." };

    public override IdentityError PasswordRequiresUpper() =>
        new() { Code = nameof(PasswordRequiresUpper), Description = "Das Passwort muss einen Großbuchstaben enthalten." };
}
