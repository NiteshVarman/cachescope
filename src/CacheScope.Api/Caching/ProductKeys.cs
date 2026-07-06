namespace CacheScope.Api.Caching;

/// <summary>Single source of truth for product cache keys, shared by the pipeline and traffic support.</summary>
public static class ProductKeys
{
    public static string For(int id) => $"product:{id}";
}
