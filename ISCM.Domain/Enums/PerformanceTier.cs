namespace ISCM.Domain.Enums;

/// <summary>
/// Phase 14.1: Performance tier affects parallelism and timeouts.
/// Used by Adaptive Execution Engine (14.2) to adjust scan behavior.
/// </summary>
public enum PerformanceTier
{
    /// <summary>
    /// High-performance environment (SSD, 4+ cores, native OS).
    /// MaxDegreeOfParallelism = Environment.ProcessorCount
    /// </summary>
    Fast,

    /// <summary>
    /// Medium-performance environment (HDD, 2-3 cores).
    /// MaxDegreeOfParallelism = max(2, ProcessorCount / 2)
    /// </summary>
    Medium,

    /// <summary>
    /// Low-performance environment (VM, single core, resource-constrained).
    /// MaxDegreeOfParallelism = 1 (sequential execution)
    /// </summary>
    Slow
}