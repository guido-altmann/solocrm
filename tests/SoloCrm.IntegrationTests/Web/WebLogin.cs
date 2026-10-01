using System.Text.RegularExpressions;

namespace SoloCrm.IntegrationTests.Web;

/// <summary>Signs in through the real login form (antiforgery token included).</summary>
public static partial class WebLogin
{
    public static async Task<HttpResponseMessage> PostAsync(HttpClient client, string email, string password, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(client);

        var loginPage = await client.GetStringAsync("/Account/Login", ct);
        var token = AntiforgeryTokenRegex().Match(loginPage).Groups[1].Value;
        token.Should().NotBeEmpty();

        return await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "login",
            ["__RequestVerificationToken"] = token,
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        }), ct);
    }

    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""")]
    private static partial Regex AntiforgeryTokenRegex();
}
