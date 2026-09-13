namespace ISCM.Domain.ValueObjects;

/// <summary>
/// Phase 14.2: Immutable execution profile computed from EnvironmentProfile.
/// Contains all runtime parameters adjusted for current environment.
/// </summary>
public record ScanExecutionProfile
{
    /// <summary>
    /// Effective max degree of parallelism after environment-based adjustment.
    /// </summary>
    public required int EffectiveMaxDegreeOfParallelism { get; init; }

    /// <summary>
    /// Effective timeout for individual check execution.
    /// </summary>
    public required TimeSpan EffectiveCheckTimeout { get; init; }

    /// <summary>
    /// Effective timeout for parser operations.
    /// </summary>
    public required TimeSpan EffectiveParserTimeout { get; init; }

    /// <summary>
    /// List of check IDs that should be skipped due to missing tools/permissions.
    /// </summary>
    public required IReadOnlyList<string> ChecksToSkip { get; init; }

    /// <summary>
    /// Human-readable reasoning for each decision made.
    /// </summary>
    public required IReadOnlyList<string> Reasoning { get; init; }

    /// <summary>
    /// Timestamp when this profile was computed.
    /// </summary>
    public required DateTimeOffset ComputedAt { get; init; }

    /// <summary>
    /// True if any checks will be skipped.
    /// </summary>
    public bool HasSkippedChecks => ChecksToSkip.Count > 0;

    /// <summary>
    /// True if running in reduced parallelism mode (Slow tier = sequential).
    /// </summary>
    public bool IsReducedParallelismMode => EffectiveMaxDegreeOfParallelism == 1;
}