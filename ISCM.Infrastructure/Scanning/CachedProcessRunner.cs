namespace ISCM.Infrastructure.Scanning;

using ISCM.Application.Interfaces;
using ISCM.Domain.ValueObjects;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Phase 14.3: Caching implementation of IProcessCacheService.
/// Caches ProcessResult by normalized command+arguments key.
///
/// Features:
/// - TTL-based invalidation from ScannerConfiguration.CacheMaxAgeMinutes
/// - Thread-safe with ConcurrentDictionary
/// - Cache statistics tracking (hits, misses, saved time)
/// - Pattern-based invalidation (e.g., "net" invalidates all net commands)
///
/// Does NOT cache failed results (ExitCode != 0) to avoid caching transient errors.
/// </summary>
public class CachedProcessRunner : IProcessCacheService
{
    private readonly IProcessRunner _innerRunner;
    private readonly IScannerConfigurationService _configService;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    // Stats tracking (thread-safe via Interlocked)
    private int _cacheHits = 0;
    private int _cacheMisses = 0;
    private long _totalSavedMs = 0;

    public CachedProcessRunner(
        IProcessRunner innerRunner,
        IScannerConfigurationService configService)
    {
        _innerRunner = innerRunner ?? throw new ArgumentNullException(nameof(innerRunner));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
    }

    public async Task<ProcessResult> GetOrRunAsync(
        string command,
        string arguments,
        bool forceRefresh = false)
    {
        var cacheKey = NormalizeCacheKey(command, arguments);
        var maxAge = _configService.GetCacheMaxAge();

        // Check cache (unless force refresh)
        if (!forceRefresh && _cache.TryGetValue(cacheKey, out var cached))
        {
            var age = DateTimeOffset.UtcNow - cached.CachedAt;
            if (age < maxAge)
            {
                Interlocked.Increment(ref _cacheHits);
                Interlocked.Add(ref _totalSavedMs, cached.Result.DurationMs);
                return cached.Result;
            }

            // Expired - remove from cache
            _cache.TryRemove(cacheKey, out _);
        }

        // Cache miss - execute the command
        Interlocked.Increment(ref _cacheMisses);
        var result = await _innerRunner.RunAsync(command, arguments);

        // Only cache successful results
        if (result.Success)
        {
            _cache[cacheKey] = new CacheEntry
            {
                Result = result,
                CachedAt = DateTimeOffset.UtcNow
            };
        }

        return result;
    }

    public void Invalidate(string commandPattern)
    {
        if (string.IsNullOrWhiteSpace(commandPattern))
            return;

        var normalizedPattern = commandPattern.Trim().ToLowerInvariant();
        var keysToRemove = _cache.Keys
            .Where(k => k.StartsWith(normalizedPattern, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var key in keysToRemove)
        {
            _cache.TryRemove(key, out _);
        }
    }

    public void InvalidateAll()
    {
        _cache.Clear();
    }

    public ProcessCacheStats GetStats()
    {
        var total = _cacheHits + _cacheMisses;
        return new ProcessCacheStats
        {
            TotalEntries = _cache.Count,
            CacheHits = _cacheHits,
            CacheMisses = _cacheMisses,
            TotalSavedMs = TimeSpan.FromMilliseconds(_totalSavedMs),
            HitRate = total > 0 ? (double)_cacheHits / total : 0
        };
    }

    public bool IsCached(string command, string arguments)
    {
        var cacheKey = NormalizeCacheKey(command, arguments);
        if (!_cache.TryGetValue(cacheKey, out var cached))
            return false;

        var maxAge = _configService.GetCacheMaxAge();
        var age = DateTimeOffset.UtcNow - cached.CachedAt;
        return age < maxAge;
    }

    /// <summary>
    /// Normalizes command and arguments into a consistent cache key.
    /// Trims whitespace and lowercases the command part.
    /// Arguments are preserved as-is (case-sensitive for some commands).
    /// </summary>
    private static string NormalizeCacheKey(string command, string arguments)
    {
        var normalizedCmd = command?.Trim().ToLowerInvariant() ?? string.Empty;
        var normalizedArgs = arguments?.Trim() ?? string.Empty;
        return $"{normalizedCmd} {normalizedArgs}";
    }

    private record CacheEntry
    {
        public required ProcessResult Result { get; init; }
        public required DateTimeOffset CachedAt { get; init; }
    }
}