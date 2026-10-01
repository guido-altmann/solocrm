using SoloCrm.Application.Features.Common;

namespace SoloCrm.Web.Endpoints;

/// <summary>The flat address fields of contacts and organizations in the REST API (iteration 5 decision 14).</summary>
internal static class ApiAddress
{
    public static readonly string[] Fields = ["street", "street2", "postalCode", "city", "region", "countryCode"];

    public static AddressData Patch(MergePatch patch, AddressData current) => new(
        patch.Value("street", current.Street),
        patch.Value("street2", current.Street2),
        patch.Value("postalCode", current.PostalCode),
        patch.Value("city", current.City),
        patch.Value("region", current.Region),
        patch.Value("countryCode", current.CountryCode));
}
