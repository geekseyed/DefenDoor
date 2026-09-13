namespace ISCM.Application.Interfaces;

using ISCM.Domain.ValueObjects;

/// <summary>
/// Phase 14.3: Caching layer for process execution results.
/// Caches ProcessResult by command+arguments key with TTL-based invalidation.
///
/// Thread-safe and designed for singleton lifetime.
/// TTL comes from ScannerConfiguration.CacheMaxAgeMinutes.
/// </summary>
public interface IProcessCacheService
{
    /// <summary>
    /// Gets cached result or runs the process and caches the result.
    /// Cache key is computed from command + arguments (normalized).
    /// </summary>
    /// <param name="command">Executable name</param>
    /// <param name="arguments">Command arguments</param>
    /// <param name="forceRefresh">If true, ignores cache and runs fresh</param>
    /// <returns>ProcessResult (cached or fresh)</returns>
    Task<ProcessResult> GetOrRunAsync(string command, string arguments, bool forceRefresh = false);

    /// <summary>
    /// Invalidates all cache entries matching the pattern.
    /// Pattern can be exact command or prefix (e.g., "net" invalidates all net commands).
    /// </summary>
    void Invalidate(string commandPattern);

    /// <summary>
    /// Clears entire cache.
    /// </summary>
    void InvalidateAll();

    /// <summary>
    /// Returns cache statistics for monitoring.
    /// </summary>
    ProcessCacheStats GetStats();

    /// <summary>
    /// Checks if a specific command+args is cached and fresh.
    /// </summary>
    bool IsCached(string command, string arguments);
}