namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-08: Runtime Diagnostics & Memory Profiling Models
/// </summary>

public class RuntimeDiagnosticSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime? EndTime { get; set; }

    public List<MemorySnapshot> MemorySnapshots { get; set; } = new();
    public List<PerformanceMetric> PerformanceMetrics { get; set; } = new();

    public ResourceBottleneck? DetectedBottleneck { get; set; }
}

public class MemorySnapshot
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public long WorkingSet64 { get; set; } // Bytes
    public long PrivateMemorySize64 { get; set; } // Bytes
    public long GCHeapMemory { get; set; } // Bytes (Estimated)
    public int GCGen0Collections { get; set; }
    public int GCGen1Collections { get; set; }
    public int GCGen2Collections { get; set; }

    // Calculated
    public double MemoryMB => WorkingSet64 / (1024.0 * 1024.0);
}

public class PerformanceMetric
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public TimeSpan TotalProcessorTime { get; set; }
    public double CpuUsagePercent { get; set; } // 0.0 to 100.0
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public long IOReadBytes { get; set; }
    public long IOWriteBytes { get; set; }
}

public class ResourceBottleneck
{
    public ResourceBottleneckType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public double SeverityScore { get; set; } // 0.0 to 1.0
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public List<string> RelatedTestIds { get; set; } = new();
}

public enum ResourceBottleneckType
{
    Unknown,
    MemoryPressure,      // High memory usage or frequent GC
    CpuSaturation,       // CPU near 100%
    ThreadStarvation,    // Too many threads / context switches
    IoBottleneck,        // High disk/network IO wait
    HandleLeak           // Growing handle count
}