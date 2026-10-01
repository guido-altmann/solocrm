namespace SoloCrm.Application.Abstractions;

/// <summary>
/// Encrypts secrets that must be readable again later, e.g. webhook signing secrets (ADR-010).
/// Implemented with ASP.NET Core Data Protection; keys live on the volume <c>/app/keys</c> (ADR-009).
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    string Unprotect(string protectedValue);
}
