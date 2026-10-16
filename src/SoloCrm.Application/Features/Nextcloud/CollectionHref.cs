namespace SoloCrm.Application.Features.Nextcloud;

/// <summary>Builds resource paths inside a collection.</summary>
public static class CollectionHref
{
    public static string Combine(string collectionHref, string resourceName) =>
        collectionHref.EndsWith('/') ? collectionHref + resourceName : $"{collectionHref}/{resourceName}";
}
