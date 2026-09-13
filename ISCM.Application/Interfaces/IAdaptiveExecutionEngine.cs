namespace ISCM.Application.Interfaces;

using ISCM.Domain.ValueObjects;

/// <summary>
/// Phase 14.2: Adaptive execution engine that adjusts scan behavior
/// based on environment capabilities detected by IEnvironmentDetector.
///
/// Uses EnvironmentProfile to determine:
/// - Optimal parallelism level (PerformanceTier-based)
/// - Timeout adjustments (Slow tier = longer timeouts)
/// - Checks to skip (due to missing tools/permissions)
///
/// Thread-safe and caches result for service lifetime.
/// </summary>
public interface IAdaptiveExecutionEngine
{
    /// <summary>
    /// Computes execution profile based on current environment.
    /// Result is cached and refreshed only if cache is invalidated.
    /// </summary>
    Task<ScanExecutionProfile> GetExecutionProfileAsync();

    /// <summary>
    /// Quick access to effective parallelism level.
    /// Uses cached profile if available, otherwise quick computation.
    /// </summary>
    int GetEffectiveMaxDegreeOfParallelism();

    /// <summary>
    /// Checks if a specific check should be skipped due to environment limitations.
    /// </summary>
    bool ShouldSkipCheck(string checkId);

    /// <summary>
    /// Returns cached profile if available, otherwise null.
    /// </summary>
    ScanExecutionProfile? GetCachedProfile();
}