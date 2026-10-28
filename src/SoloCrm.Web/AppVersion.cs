using System.Reflection;

namespace SoloCrm.Web;

/// <summary>The version of the running app, derived from git tags by GitVersion at build time.</summary>
public static class AppVersion
{
    public static string Current { get; } = Parse(
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>Strips the build metadata after '+' (commit hash) and falls back to "dev" if no version is set.</summary>
    public static string Parse(string? informationalVersion)
    {
        var version = informationalVersion?.Split('+')[0];
        return string.IsNullOrWhiteSpace(version) ? "dev" : version;
    }
}
