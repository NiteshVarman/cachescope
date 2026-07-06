namespace CacheScope.Shared;

/// <summary>Whether a layer satisfied the request (Hit) or passed it down (Miss).</summary>
public enum CacheOutcome
{
    Miss = 0,
    Hit = 1
}
