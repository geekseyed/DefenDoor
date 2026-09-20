using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class TrendAnalysisServiceTests
{
    private readonly TrendAnalysisService _service;

    public TrendAnalysisServiceTests()
    {
        _service = new TrendAnalysisService();
    }

    [Fact]
    public void AnalyzeTrend_WithInsufficientData_ReturnsInsufficientDataStatus()
    {
        // Arrange
        var history = new List<TestHistoryRecord>
        {
            new TestHistoryRecord { ExecutedAt = DateTime.UtcNow, WasSuccessful = true, DurationMs = 100 },
            new TestHistoryRecord { ExecutedAt = DateTime.UtcNow.AddDays(-1), WasSuccessful = true, DurationMs = 100 }
        };

        // Act
        var result = _service.AnalyzeTrend(history);

        // Assert
        result.Status.Should().Be(StabilityStatus.InsufficientData);
    }

    [Fact]
    public void AnalyzeTrend_WithIncreasingDuration_ReturnsDegradingPerformance()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var history = new List<TestHistoryRecord>
        {
            new TestHistoryRecord { ExecutedAt = now.AddDays(-3), WasSuccessful = true, DurationMs = 100 },
            new TestHistoryRecord { ExecutedAt = now.AddDays(-2), WasSuccessful = true, DurationMs = 200 },
            new TestHistoryRecord { ExecutedAt = now.AddDays(-1), WasSuccessful = true, DurationMs = 300 },
            new TestHistoryRecord { ExecutedAt = now, WasSuccessful = true, DurationMs = 400 }
        };

        // Act
        var result = _service.AnalyzeTrend(history);

        // Assert
        result.Status.Should().Be(StabilityStatus.DegradingPerformance);
        result.DurationTrendSlope.Should().BeGreaterThan(0);
    }

    [Fact]
    public void AnalyzeTrend_WithStableData_ReturnsStable()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var history = new List<TestHistoryRecord>
        {
            new TestHistoryRecord { ExecutedAt = now.AddDays(-3), WasSuccessful = true, DurationMs = 100 },
            new TestHistoryRecord { ExecutedAt = now.AddDays(-2), WasSuccessful = true, DurationMs = 105 },
            new TestHistoryRecord { ExecutedAt = now.AddDays(-1), WasSuccessful = true, DurationMs = 98 },
            new TestHistoryRecord { ExecutedAt = now, WasSuccessful = true, DurationMs = 102 },
            new TestHistoryRecord { ExecutedAt = now.AddHours(-1), WasSuccessful = true, DurationMs = 101 }
        };

        // Act
        var result = _service.AnalyzeTrend(history);

        // Assert
        result.Status.Should().Be(StabilityStatus.Stable);
    }
}