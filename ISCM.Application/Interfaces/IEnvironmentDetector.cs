namespace ISCM.Application.Interfaces;

using ISCM.Domain.Enums;
using ISCM.Domain.ValueObjects;

/// <summary>
/// Phase 14.1: Detects environment capabilities before scan execution.
/// Used by Adaptive Execution Engine (14.2) to adjust scan behavior.
///
/// Implementation must be safe to call multiple times and cache results.
/// All detection methods must not throw exceptions under normal conditions.
/// </summary>
public interface IEnvironmentDetector
{
    /// <summary>
    /// Performs comprehensive environment detection.
    /// Result is cached for the lifetime of the service.
    /// Safe to call multiple times.
    /// </summary>
    Task<EnvironmentProfile> DetectEnvironmentAsync();

    /// <summary>
    /// Quick permission check without full detection.
    /// </summary>
    bool HasPermission(PermissionType permission);

    /// <summary>
    /// Quick tool availability check without full detection.
    /// </summary>
    bool IsToolAvailable(ToolType tool);

    /// <summary>
    /// Returns detected performance tier.
    /// </summary>
    PerformanceTier GetPerformanceTier();

    /// <summary>
    /// Returns cached profile if detection has been run, otherwise null.
    /// </summary>
    EnvironmentProfile? GetCachedProfile();
}