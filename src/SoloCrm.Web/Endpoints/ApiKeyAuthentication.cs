using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using SoloCrm.Application.Abstractions;
using SoloCrm.Application.Features.ApiKeys;

namespace SoloCrm.Web.Endpoints;

/// <summary>
/// Authentication scheme for the REST API (US-17 AK2): header <c>X-Api-Key</c>, separate from the cookie login.
/// The API policy accepts only this scheme, so a signed-in browser cannot call the API with its cookie (no CSRF).
/// </summary>
public static class ApiKeyAuthentication
{
    public const string Scheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";
    public const string PolicyName = "api";

    public static AuthenticationBuilder AddApiKey(this AuthenticationBuilder builder) =>
        builder.AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(Scheme, null);

    public static AuthorizationBuilder AddApiKeyPolicy(this AuthorizationBuilder builder) =>
        builder.AddPolicy(PolicyName, policy => policy.AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser());
}

internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ICommandHandler<AuthenticateApiKey.Command, AuthenticateApiKey.Result> authenticate)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthentication.HeaderName, out var values))
        {
            return AuthenticateResult.NoResult();
        }

        var result = await authenticate.Handle(new AuthenticateApiKey.Command(values.ToString()), Context.RequestAborted);
        if (result.IsFailure)
        {
            // The key itself is never logged (SPEC 6).
            return AuthenticateResult.Fail("Invalid or revoked API key.");
        }

        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, result.Value.ApiKeyId.ToString()),
            new(ClaimTypes.Name, result.Value.Name),
        ];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = $"{ApiKeyAuthentication.Scheme} header=\"{ApiKeyAuthentication.HeaderName}\"";
        await TypedResults.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Nicht authentifiziert",
                detail: $"Fehlender, ungültiger oder widerrufener API-Key (Header {ApiKeyAuthentication.HeaderName}).")
            .ExecuteAsync(Context);
    }
}
