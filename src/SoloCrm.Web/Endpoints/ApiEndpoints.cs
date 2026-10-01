using System.Text.Json.Serialization;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace SoloCrm.Web.Endpoints;

/// <summary>
/// The REST API under <c>/api/v1</c> (US-17, SPEC 5): API-key authentication, 60 requests per minute and key,
/// Problem Details, OpenAPI document and Scalar UI for the signed-in user (iteration 5 decision 5).
/// Endpoints call the same handlers as the UI and never touch the DbContext.
/// </summary>
public static class ApiEndpoints
{
    public const string BasePath = "/api/v1";
    public const string DocumentName = "v1";

    public static IServiceCollection AddCrmApi(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddRateLimiter(options => options.AddPolicy<string, ApiRateLimiting>(ApiRateLimiting.PolicyName));

        services.AddOpenApi(DocumentName, options =>
        {
            options.ShouldInclude = description => description.RelativePath?.StartsWith("api/", StringComparison.Ordinal) == true;
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "SoloCRM API",
                    Version = DocumentName,
                    Description = "REST-API von SoloCRM. Authentifizierung per Header X-Api-Key (Einstellungen → API-Keys), "
                        + "höchstens 60 Anfragen pro Minute. Fehler als Problem Details (RFC 9457), PATCH als JSON Merge Patch (RFC 7396).",
                };
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[ApiKeyAuthentication.Scheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = ApiKeyAuthentication.HeaderName,
                    Description = "API-Key aus den Einstellungen (scrm_…).",
                };
                document.Security =
                [
                    new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(ApiKeyAuthentication.Scheme, document)] = [] },
                ];
                return Task.CompletedTask;
            });
        });

        return services;
    }

    public static IEndpointRouteBuilder MapCrmApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup(BasePath)
            .RequireAuthorization(ApiKeyAuthentication.PolicyName)
            .RequireRateLimiting(ApiRateLimiting.PolicyName);

        api.MapContactEndpoints();
        api.MapOrganizationEndpoints();
        api.MapOpportunityEndpoints();
        api.MapActivityEndpoints();
        api.MapTaskEndpoints();
        api.MapStageEndpoints();
        api.MapFallback(() => ApiResults.NotFound("Unbekannter Endpunkt.")).ExcludeFromDescription();

        // Without explicit authorization metadata, the fallback policy (cookie login) protects both (decision 5).
        app.MapOpenApi();
        app.MapScalarApiReference(options => options
            .WithTitle("SoloCRM API")
            .AddPreferredSecuritySchemes(ApiKeyAuthentication.Scheme));

        return app;
    }
}
