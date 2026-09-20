using ISCM.BugFinder.Core.Models;
using System.Linq;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-08.3: Bottleneck Detection Engine
/// Analyzes resource metrics to identify performance bottlenecks and correlations with failures.
/// </summary>
public class BottleneckDetectionService
{
    // Thresholds
    private const double HighCpuThreshold = 90.0; // Percent
    private const long HighMemoryThresholdMB = 1024; // 1GB
    private const int HighHandleCount = 10000;
    private const int MaxGCGen2Diff = 5; // Allowed Gen2 collections during a single test

    public ResourceBottleneck? DetectBottleneck(RuntimeDiagnosticSession session)
    {
        if (session.MemorySnapshots.Count == 0 || session.PerformanceMetrics.Count == 0)
            return null;

        // 1. Check for Memory Pressure
        var maxMemoryMB = session.MemorySnapshots.Max(s => s.MemoryMB);
        if (maxMemoryMB > HighMemoryThresholdMB)
        {
            return new ResourceBottleneck
            {
                Type = ResourceBottleneckType.MemoryPressure,
                Description = $"Peak memory usage ({maxMemoryMB:F1} MB) exceeded threshold ({HighMemoryThresholdMB} MB).",
                SeverityScore = Math.Min(1.0, maxMemoryMB / (HighMemoryThresholdMB * 2)),
                DetectedAt = DateTime.UtcNow
            };
        }

        // 2. Check for Excessive GC (Gen2 collections indicate heavy pressure)
        var gen2Collections = session.MemorySnapshots.Last().GCGen2Collections -
                              session.MemorySnapshots.First().GCGen2Collections;

        if (gen2Collections > MaxGCGen2Diff)
        {
            return new ResourceBottleneck
            {
                Type = ResourceBottleneckType.MemoryPressure,
                Description = $"Excessive Gen2 GC collections ({gen2Collections}) detected during session.",
                SeverityScore = Math.Min(1.0, gen2Collections / 20.0),
                DetectedAt = DateTime.UtcNow
            };
        }

        // 3. Check for CPU Saturation
        var avgCpu = session.PerformanceMetrics.Average(m => m.CpuUsagePercent);
        if (avgCpu > HighCpuThreshold)
        {
            return new ResourceBottleneck
            {
                Type = ResourceBottleneckType.CpuSaturation,
                Description = $"Average CPU usage ({avgCpu:F1}%) exceeded threshold ({HighCpuThreshold}%).",
                SeverityScore = Math.Min(1.0, avgCpu / 100.0),
                DetectedAt = DateTime.UtcNow
            };
        }

        // 4. Check for Handle Leak
        var startHandles = session.PerformanceMetrics.First().HandleCount;
        var endHandles = session.PerformanceMetrics.Last().HandleCount;
        var handleGrowth = endHandles - startHandles;

        if (endHandles > HighHandleCount || handleGrowth > 1000)
        {
            return new ResourceBottleneck
            {
                Type = ResourceBottleneckType.HandleLeak,
                Description = $"Handle count grew by {handleGrowth} (Total: {endHandles}). Possible leak.",
                SeverityScore = Math.Min(1.0, handleGrowth / 5000.0),
                DetectedAt = DateTime.UtcNow
            };
        }

        return null; // No bottleneck detected
    }

    public List<ResourceBottleneck> CorrelateWithFailures(List<Failure> failures, List<ResourceBottleneck> bottlenecks)
    {
        // Simple correlation: attach related test IDs if failure time overlaps with bottleneck detection time
        // For now, just return the bottlenecks; advanced correlation logic can be added later.
        return bottlenecks;
    }
}