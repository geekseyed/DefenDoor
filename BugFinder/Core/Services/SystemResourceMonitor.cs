using System.Diagnostics;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-08.2: System Resource Monitor Service
/// Captures real-time memory, CPU, and handle metrics for the current process.
/// Cross-platform compatible (no PerformanceCounter dependency).
/// </summary>
public class SystemResourceMonitor : IDisposable
{
    private readonly Process _process;
    private DateTime _lastCpuCheckTime;
    private TimeSpan _lastCpuTotalTime;
    private int _processorCount;
    private bool _disposed;

    public SystemResourceMonitor()
    {
        _process = Process.GetCurrentProcess();
        _processorCount = Environment.ProcessorCount;

        // Initialize CPU tracking
        _process.Refresh();
        _lastCpuCheckTime = DateTime.UtcNow;
        _lastCpuTotalTime = _process.TotalProcessorTime;
    }

    public MemorySnapshot CaptureMemorySnapshot()
    {
        _process.Refresh();
        return new MemorySnapshot
        {
            Timestamp = DateTime.UtcNow,
            WorkingSet64 = _process.WorkingSet64,
            PrivateMemorySize64 = _process.PrivateMemorySize64,
            GCHeapMemory = GC.GetTotalMemory(forceFullCollection: false),
            GCGen0Collections = GC.CollectionCount(0),
            GCGen1Collections = GC.CollectionCount(1),
            GCGen2Collections = GC.CollectionCount(2)
        };
    }

    public PerformanceMetric CapturePerformanceMetric()
    {
        _process.Refresh();

        var now = DateTime.UtcNow;
        var totalTime = _process.TotalProcessorTime;

        // Calculate CPU usage percentage based on time delta
        var timeDelta = (now - _lastCpuCheckTime).TotalMilliseconds;
        var cpuDelta = (totalTime - _lastCpuTotalTime).TotalMilliseconds;

        double cpuPercent = 0.0;
        if (timeDelta > 0)
        {
            // CPU% = (CPU Time Delta / Time Delta) / Processor Count * 100
            cpuPercent = (cpuDelta / timeDelta) / _processorCount * 100.0;
            cpuPercent = Math.Min(100.0, Math.Max(0.0, cpuPercent));
        }

        // Update trackers for next call
        _lastCpuCheckTime = now;
        _lastCpuTotalTime = totalTime;

        return new PerformanceMetric
        {
            Timestamp = now,
            TotalProcessorTime = totalTime,
            CpuUsagePercent = Math.Round(cpuPercent, 2),
            ThreadCount = _process.Threads.Count,
            HandleCount = _process.HandleCount,
            IOReadBytes = 0, // Not reliably cross-platform without extra packages
            IOWriteBytes = 0
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _process.Dispose();
            _disposed = true;
        }
    }
}