using Microsoft.AspNetCore.DataProtection;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Infrastructure.Webhooks;

/// <summary>
/// Encrypts secrets with ASP.NET Core Data Protection (ADR-010). Losing the key ring (volume <c>/app/keys</c>)
/// makes them unreadable; the webhook then needs a new secret (ADR-009).
/// </summary>
internal sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("SoloCrm.WebhookSecrets");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}
