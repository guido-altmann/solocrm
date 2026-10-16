using System.Security.Cryptography;
using System.Text;

namespace SoloCrm.Application.Features.Nextcloud;

/// <summary>SHA-256 of a rendered payload (hex, lower case); a changed hash means the copy in Nextcloud is outdated.</summary>
public static class PayloadHash
{
    public static string Of(string payload) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
}
