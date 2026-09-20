using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class BottleneckDetectionServiceTests
{
    private readonly BottleneckDetectionService _service;

    public BottleneckDetectionServiceTests()
    {
        _service = new BottleneckDetectionService();
    }

    [Fact]
    public void DetectBottleneck_WithHighMemory_ReturnsMemoryPressure()
    {
        // Arrange
        var session = new RuntimeDiagnosticSession
        {
            MemorySnapshots = new List<MemorySnapshot>
            {
                new MemorySnapshot { WorkingSet64 = 2L * 1024 * 1024 * 1024 } // 2GB
            },
            PerformanceMetrics = new List<PerformanceMetric> { new PerformanceMetric() }
        };

        // Act
        var bottleneck = _service.DetectBottleneck(session);

        // Assert
        bottleneck.Should().NotBeNull();
        bottleneck!.Type.Should().Be(ResourceBottleneckType.MemoryPressure);
        bottleneck.Description.Should().Contain("2048");
    }

    [Fact]
    public void DetectBottleneck_WithExcessiveGC_ReturnsMemoryPressure()
    {
        // Arrange
        var session = new RuntimeDiagnosticSession
        {
            MemorySnapshots = new List<MemorySnapshot>
            {
                new MemorySnapshot { GCGen2Collections = 0 },
                new MemorySnapshot { GCGen2Collections = 10 } // Diff > 5
            },
            PerformanceMetrics = new List<PerformanceMetric> { new PerformanceMetric(), new PerformanceMetric() }
        };

        // Act
        var bottleneck = _service.DetectBottleneck(session);

        // Assert
        bottleneck.Should().NotBeNull();
        bottleneck!.Type.Should().Be(ResourceBottleneckType.MemoryPressure);
        bottleneck.Description.Should().Contain("Gen2");
    }

    [Fact]
    public void DetectBottleneck_WithNormalMetrics_ReturnsNull()
    {
        // Arrange
        var session = new RuntimeDiagnosticSession
        {
            MemorySnapshots = new List<MemorySnapshot>
            {
                new MemorySnapshot { WorkingSet64 = 100 * 1024 * 1024, GCGen2Collections = 1 } // 100MB, low GC
            },
            PerformanceMetrics = new List<PerformanceMetric>
            {
                new PerformanceMetric { CpuUsagePercent = 10.0, HandleCount = 100 }
            }
        };

        // Act
        var bottleneck = _service.DetectBottleneck(session);

        // Assert
        bottleneck.Should().BeNull();
    }
}