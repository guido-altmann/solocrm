namespace SoloCrm.Infrastructure.Enrichment;

/// <summary>
/// Configuration section <c>Ai</c>: the Foundry project and the agent in it (ADR-016, point 6). The sign-in is not
/// configured here: <c>DefaultAzureCredential</c> uses the standard Azure variables (<c>AZURE_TENANT_ID</c>,
/// <c>AZURE_CLIENT_ID</c>, <c>AZURE_CLIENT_SECRET</c> for a service principal) or, locally, the Azure CLI login.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Project endpoint, e.g. <c>https://{resource}.services.ai.azure.com/api/projects/{project}</c>.</summary>
    public string? ProjectEndpoint { get; set; }

    /// <summary>Name of the agent in the project.</summary>
    public string? AgentName { get; set; }

    public bool IsConfigured =>
        Uri.TryCreate(ProjectEndpoint, UriKind.Absolute, out var endpoint) && endpoint.Scheme == Uri.UriSchemeHttps
        && !string.IsNullOrWhiteSpace(AgentName);
}
