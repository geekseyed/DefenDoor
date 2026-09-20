using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class SystemResourceMonitorTests
{
    [Fact]
    public void CaptureMemorySnapshot_ReturnsValidData()
    {
        // Arrange
        using var monitor = new SystemResourceMonitor();

        // Act
        var snapshot = monitor.CaptureMemorySnapshot();

        // Assert
        snapshot.WorkingSet64.Should().BeGreaterThan(0);
        snapshot.PrivateMemorySize64.Should().BeGreaterThan(0);
        snapshot.GCHeapMemory.Should().BeGreaterThan(0);
        snapshot.MemoryMB.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CapturePerformanceMetric_ReturnsValidData()
    {
        // Arrange
        using var monitor = new SystemResourceMonitor();

        // Act
        var metric = monitor.CapturePerformanceMetric();

        // Assert
        metric.CpuUsagePercent.Should().BeGreaterOrEqualTo(0).And.BeLessOrEqualTo(100);
        metric.ThreadCount.Should().BeGreaterThan(0);
        metric.HandleCount.Should().BeGreaterThan(0);
        metric.TotalProcessorTime.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void Monitor_Dispose_DoesNotThrow()
    {
        // Arrange & Act
        var monitor = new SystemResourceMonitor();

        // Assert
        monitor.Invoking(m => m.Dispose()).Should().NotThrow();
    }
}