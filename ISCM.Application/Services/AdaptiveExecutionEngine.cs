namespace ISCM.Application.Services;

using ISCM.Application.Interfaces;
using ISCM.Domain.Entities;
using ISCM.Domain.Enums;
using ISCM.Domain.ValueObjects;

/// <summary>
/// Phase 14.2: Adaptive execution engine implementation.
/// Computes ScanExecutionProfile from EnvironmentProfile and ScannerConfiguration.
///
/// Decisions made:
///   - Parallelism: User config > Environment tier
///   - Timeouts: Base timeout × tier multiplier
///   - Skipped checks: Based on missing tools/permissions
///
/// Thread-safe with lazy caching.
/// </summary>
public class AdaptiveExecutionEngine : IAdaptiveExecutionEngine
{
    private readonly IEnvironmentDetector _environmentDetector;
    private readonly IScannerConfigurationService _configService;
    private ScanExecutionProfile? _cachedProfile;
    private readonly object _lock = new();

    // ═══════════════════════════════════════════════════════════
    // Check-to-Tool mapping
    // TODO: Phase 14.3 - Move this to CheckDefinition metadata
    // ═══════════════════════════════════════════════════════════
    private static readonly Dictionary<string, ToolType[]> CheckToolRequirements = new()
    {
        // Advanced Audit uses auditpol.exe
        { "AUD-001", new[] { ToolType.Auditpol } },

        // Account Lockout and Password Length use net accounts
        { "LCK-001", new[] { ToolType.NetExe } },
        { "PWD-001", new[] { ToolType.NetExe } },

        // User Rights uses secedit
        { "URA-001", new[] { ToolType.Secedit } },
    };

    public AdaptiveExecutionEngine(
        IEnvironmentDetector environmentDetector,
        IScannerConfigurationService configService)
    {
        _environmentDetector = environmentDetector ?? throw new ArgumentNullException(nameof(environmentDetector));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
    }

    public async Task<ScanExecutionProfile> GetExecutionProfileAsync()
    {
        lock (_lock)
        {
            if (_cachedProfile != null)
                return _cachedProfile;
        }

        var envProfile = await _environmentDetector.DetectEnvironmentAsync();
        var config = _configService.GetCurrentConfiguration();

        var reasoning = new List<string>();
        reasoning.Add($"Environment detected at {envProfile.DetectedAt:O}");
        reasoning.Add($"Machine: {envProfile.Hardware.MachineName}, CPUs: {envProfile.Hardware.ProcessorCount}, VM: {envProfile.Hardware.IsVirtualMachine}");

        // Compute parallelism
        var parallelism = ComputeParallelism(envProfile, config, reasoning);

        // Compute timeouts
        var (checkTimeout, parserTimeout) = ComputeTimeouts(envProfile, config, reasoning);

        // Determine checks to skip
        var checksToSkip = DetermineChecksToSkip(envProfile, reasoning);

        var profile = new ScanExecutionProfile
        {
            EffectiveMaxDegreeOfParallelism = parallelism,
            EffectiveCheckTimeout = checkTimeout,
            EffectiveParserTimeout = parserTimeout,
            ChecksToSkip = checksToSkip,
            Reasoning = reasoning,
            ComputedAt = DateTimeOffset.UtcNow
        };

        lock (_lock)
        {
            _cachedProfile = profile;
        }

        return profile;
    }

    public int GetEffectiveMaxDegreeOfParallelism()
    {
        var profile = GetCachedProfile();
        if (profile != null)
            return profile.EffectiveMaxDegreeOfParallelism;

        // Quick computation without full detection
        var config = _configService.GetCurrentConfiguration();

        if (config.MaxDegreeOfParallelism > 0)
            return config.MaxDegreeOfParallelism;

        var envProfile = _environmentDetector.GetCachedProfile();
        if (envProfile == null)
            return Environment.ProcessorCount;

        return envProfile.PerformanceTier switch
        {
            PerformanceTier.Fast => Environment.ProcessorCount,
            PerformanceTier.Medium => Math.Max(2, Environment.ProcessorCount / 2),
            PerformanceTier.Slow => 1,
            _ => Environment.ProcessorCount
        };
    }

    public bool ShouldSkipCheck(string checkId)
    {
        var profile = GetCachedProfile();
        return profile?.ChecksToSkip.Contains(checkId) ?? false;
    }

    public ScanExecutionProfile? GetCachedProfile()
    {
        lock (_lock)
        {
            return _cachedProfile;
        }
    }

    #region Private Methods

    private static int ComputeParallelism(
        EnvironmentProfile envProfile,
        ScannerConfiguration config,
        List<string> reasoning)
    {
        // If user explicitly set parallelism, respect it
        if (config.MaxDegreeOfParallelism > 0)
        {
            reasoning.Add($"Parallelism: {config.MaxDegreeOfParallelism} (user-configured, overrides environment)");
            return config.MaxDegreeOfParallelism;
        }

        // Environment-based adjustment
        var baseParallelism = envProfile.Hardware.ProcessorCount;

        var adjusted = envProfile.PerformanceTier switch
        {
            PerformanceTier.Fast => baseParallelism,
            PerformanceTier.Medium => Math.Max(2, baseParallelism / 2),
            PerformanceTier.Slow => 1,
            _ => baseParallelism
        };

        reasoning.Add($"Parallelism: {adjusted} (base: {baseParallelism}, tier: {envProfile.PerformanceTier})");

        return adjusted;
    }

    private static (TimeSpan CheckTimeout, TimeSpan ParserTimeout) ComputeTimeouts(
        EnvironmentProfile envProfile,
        ScannerConfiguration config,
        List<string> reasoning)
    {
        var baseCheckTimeout = TimeSpan.FromSeconds(config.CheckTimeoutSeconds);
        var baseParserTimeout = TimeSpan.FromSeconds(config.ParserTimeoutSeconds);

        // Adjust timeouts based on performance tier
        var multiplier = envProfile.PerformanceTier switch
        {
            PerformanceTier.Fast => 1.0,
            PerformanceTier.Medium => 1.5,
            PerformanceTier.Slow => 2.5,
            _ => 1.0
        };

        var checkTimeout = TimeSpan.FromSeconds(baseCheckTimeout.TotalSeconds * multiplier);
        var parserTimeout = TimeSpan.FromSeconds(baseParserTimeout.TotalSeconds * multiplier);

        reasoning.Add($"Timeouts: check={checkTimeout.TotalSeconds:F0}s, parser={parserTimeout.TotalSeconds:F0}s (multiplier: {multiplier}x)");

        return (checkTimeout, parserTimeout);
    }

    private IReadOnlyList<string> DetermineChecksToSkip(
        EnvironmentProfile envProfile,
        List<string> reasoning)
    {
        var skipList = new List<string>();

        // Check tool requirements against available tools
        foreach (var (checkId, requiredTools) in CheckToolRequirements)
        {
            foreach (var tool in requiredTools)
            {
                if (!_environmentDetector.IsToolAvailable(tool))
                {
                    if (!skipList.Contains(checkId))
                    {
                        skipList.Add(checkId);
                        reasoning.Add($"Skip {checkId}: requires {tool} (unavailable)");
                    }
                }
            }
        }

        // Warn about admin-dependent checks (don't skip, just warn)
        if (!envProfile.Permissions.IsAdmin)
        {
            reasoning.Add("Warning: Not running as Administrator - some checks may return Error status");
        }

        if (skipList.Count == 0)
        {
            reasoning.Add("No checks will be skipped - all required tools available");
        }

        return skipList;
    }

    #endregion
}