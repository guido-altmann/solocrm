namespace SoloCrm.Web.Components.Shared;

/// <summary>Routes of the detail views (SPEC 3.3 S5).</summary>
public static class Links
{
    public static string Contact(Guid id) => $"/contacts/{id}";

    public static string Organization(Guid id) => $"/organizations/{id}";

    public static string Opportunity(Guid id) => $"/opportunities/{id}";
}
