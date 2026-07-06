namespace CacheScope.Shared.Models;

/// <summary>
/// The demo domain entity. Products are the thing being cached across L2–L4.
/// Kept deliberately small — CacheScope is about the cache path, not the model.
/// </summary>
public sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }

    /// <summary>Bumped on every write; used to build the ETag so L0/L1 can revalidate.</summary>
    public int Version { get; set; } = 1;

    public DateTimeOffset UpdatedAt { get; set; }
}
