namespace SoloCrm.Application.Abstractions;

/// <summary>
/// A CardDAV/CalDAV server SoloCRM mirrors its records to (ADR-014). Implemented with plain HTTP in Infrastructure;
/// only standard DAV is used. Hrefs are absolute paths on the server (e.g. <c>/remote.php/dav/…/solocrm/</c>).
/// </summary>
public interface IDavSyncTarget
{
    /// <summary>
    /// Finds the address book „SoloCRM“ of the account (by display name) or creates it below the
    /// <c>addressbook-home-set</c>. Returns its href; doubles as the connection check.
    /// </summary>
    /// <exception cref="DavUnauthorizedException">The server rejected the credentials.</exception>
    /// <exception cref="DavException">The server is unreachable or no CardDAV server.</exception>
    Task<string> EnsureAddressBookAsync(DavAccount account, CancellationToken cancellationToken);

    /// <summary>
    /// Writes a resource. <paramref name="ifMatch"/> is the expected ETag; <c>null</c> creates the resource only if it
    /// does not exist yet (<c>If-None-Match: *</c>). <paramref name="unconditional"/> overwrites in any case.
    /// </summary>
    /// <exception cref="DavUnauthorizedException">The server rejected the credentials.</exception>
    /// <exception cref="DavException">Any other failure.</exception>
    Task<DavWriteResult> PutAsync(
        DavAccount account, string href, DavContent content, string? ifMatch, bool unconditional, CancellationToken cancellationToken);

    /// <summary>Deletes a resource; a resource that is already gone counts as deleted.</summary>
    /// <exception cref="DavUnauthorizedException">The server rejected the credentials.</exception>
    /// <exception cref="DavException">Any other failure.</exception>
    Task DeleteAsync(DavAccount account, string href, CancellationToken cancellationToken);
}

/// <summary>Credentials of one account. <see cref="ServerUrl"/> is the base address as entered.</summary>
public sealed record DavAccount(Uri ServerUrl, string UserName, string Password)
{
    // Keeps the password out of logs and debugger displays.
    public override string ToString() => $"{UserName} @ {ServerUrl}";
}

/// <summary>Payload of a resource with its media type (<c>text/vcard</c>, <c>text/calendar</c>).</summary>
public sealed record DavContent(string Body, string MediaType);

/// <summary>Outcome of a write. <see cref="ETag"/> is <c>null</c> if the server did not report one.</summary>
public sealed record DavWriteResult(bool PreconditionFailed, string? ETag)
{
    public static DavWriteResult Conflict { get; } = new(true, null);

    public static DavWriteResult Written(string? etag) => new(false, etag);
}

/// <summary>The DAV server cannot be used. The message is shown to the user and therefore German.</summary>
public class DavException : Exception
{
    public DavException()
    {
    }

    public DavException(string message)
        : base(message)
    {
    }

    public DavException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The server answered <c>401</c>: user name or app password are wrong or revoked.</summary>
public sealed class DavUnauthorizedException : DavException
{
    public DavUnauthorizedException()
        : base("Nextcloud hat die Anmeldung abgelehnt. Bitte Benutzername und App-Passwort prüfen.")
    {
    }

    public DavUnauthorizedException(string message)
        : base(message)
    {
    }

    public DavUnauthorizedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
