#pragma warning disable OPENAI001 // The Responses API types of the OpenAI SDK are marked experimental.

using System.ClientModel;
using Azure;
using Azure.AI.Extensions.OpenAI;
using Azure.Identity;
using OpenAI.Responses;
using SoloCrm.Application.Abstractions;

namespace SoloCrm.Infrastructure.Enrichment;

/// <summary>
/// Calls the agent through the agent-scoped Responses client (ADR-016). Every call is standalone and not stored by the
/// service (<c>store: false</c>), so there is no conversation or response to delete afterwards.
/// </summary>
internal sealed class FoundryAgent(ProjectResponsesClient client) : IFoundryAgent
{
    public async Task<AgentReply> AskAsync(string input, CancellationToken cancellationToken)
    {
        var options = new CreateResponseOptions { StoredOutputEnabled = false };
        options.InputItems.Add(ResponseItem.CreateUserMessageItem(input));

        ResponseResult result;
        try
        {
            result = (await client.CreateResponseAsync(options, cancellationToken)).Value;
        }
        catch (ClientResultException exception)
        {
            throw Failure(exception.Status, exception);
        }
        catch (CredentialUnavailableException exception)
        {
            throw new EnrichmentProviderException(
                "Für Azure ist keine Anmeldung eingerichtet (AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_CLIENT_SECRET).", innerException: exception);
        }
        catch (AuthenticationFailedException exception)
        {
            throw new EnrichmentProviderException(
                "Die Anmeldung bei Azure ist fehlgeschlagen. Bitte AZURE_TENANT_ID, AZURE_CLIENT_ID und AZURE_CLIENT_SECRET prüfen.", innerException: exception);
        }
        catch (RequestFailedException exception)
        {
            throw Failure(exception.Status, exception);
        }
        catch (HttpRequestException exception)
        {
            throw new EnrichmentProviderException("Der Agent ist nicht erreichbar.", isTransient: true, exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new EnrichmentProviderException("Der Agent hat nicht rechtzeitig geantwortet.", isTransient: true, exception);
        }

        var citations = new List<string>();
        var searches = 0;
        foreach (var item in result.OutputItems)
        {
            if (item is WebSearchCallResponseItem)
            {
                searches++;
            }
            else if (item is MessageResponseItem message)
            {
                citations.AddRange(message.Content
                    .SelectMany(part => part.OutputTextAnnotations ?? [])
                    .OfType<UriCitationMessageAnnotation>()
                    .Select(annotation => annotation.Uri.ToString()));
            }
        }

        return new AgentReply(
            result.GetOutputText(),
            citations,
            searches,
            result.Usage?.InputTokenCount,
            result.Usage?.OutputTokenCount,
            result.Status == ResponseStatus.Completed);
    }

    private static EnrichmentProviderException Failure(int status, Exception inner) => status switch
    {
        401 or 403 => new EnrichmentProviderException(
            "Der Zugriff auf den Agent wurde abgelehnt. Bitte die Rolle „Azure AI User“ auf dem Foundry-Projekt prüfen.", innerException: inner),
        404 => new EnrichmentProviderException("Der Agent wurde nicht gefunden. Bitte Ai__ProjectEndpoint und Ai__AgentName prüfen.", innerException: inner),
        408 or 429 or >= 500 => new EnrichmentProviderException(
            "Der Agent ist gerade nicht verfügbar oder überlastet.", isTransient: true, inner),
        _ => new EnrichmentProviderException($"Der Agent hat die Anfrage abgelehnt (HTTP {status}).", innerException: inner),
    };
}
