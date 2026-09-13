namespace ISCM.Domain.ValueObjects;

/// <summary>
/// Phase 14.3: Statistics about process cache usage.
/// Used for monitoring and debugging cache performance.
/// </summary>
public record ProcessCacheStats
{
    public required int TotalEntries { get; init; }
    public required int CacheHits { get; init; }
    public required int CacheMisses { get; init; }
    public required TimeSpan TotalSavedMs { get; init; }
    public required double HitRate { get; init; }

    /// <summary>
    /// Total requests (hits + misses).
    /// </summary>
    public int TotalRequests => CacheHits + CacheMisses;
}