using System.Text;

namespace SoloCrm.Application.Tests.Features.Import;

/// <summary>
/// A shortened HubSpot contact export with the quirks of the real one (iteration 5 decision 9): every value quoted,
/// comma-separated, duplicate column names and escaped quotes in headers.
/// </summary>
public static class HubSpotSample
{
    public const string Header =
        "\"Record ID\",\"First Name\",\"Last Name\",\"Email\",\"Work email\",\"Phone Number\",\"Mobile Phone Number\","
        + "\"Billing Contact IDs\",\"Job Title\",\"lh_current_position\",\"LinkedIn URL\",\"lh_linkedin_profile_url\","
        + "\"Company Name\",\"Website URL\",\"Original Traffic Source\",\"Lifecycle Stage\",\"Billing Contact IDs\","
        + "\"Date entered \"\"Kunde (Lifecycle Stage Pipeline)\"\"\",\"Billing Contact IDs\",\"Create Date\"";

    public static string Row(
        string recordId,
        string firstName = "",
        string lastName = "",
        string email = "",
        string workEmail = "",
        string phone = "",
        string mobile = "",
        string jobTitle = "",
        string position = "",
        string linkedIn = "",
        string linkedInProfile = "",
        string company = "",
        string website = "",
        string source = "",
        string lifecycleStage = "") =>
        string.Join(',', new[]
        {
            recordId, firstName, lastName, email, workEmail, phone, mobile, "1;2", jobTitle, position, linkedIn, linkedInProfile,
            company, website, source, lifecycleStage, "", "2026-01-01", "", "2025-12-24 10:00",
        }.Select(v => $"\"{v.Replace("\"", "\"\"", StringComparison.Ordinal)}\""));

    public static byte[] File(params string[] rows) => Encoding.UTF8.GetBytes(string.Join("\r\n", [Header, .. rows]) + "\r\n");
}
