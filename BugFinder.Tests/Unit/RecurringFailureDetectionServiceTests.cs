using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ISCM.Tests.Unit.BugFinder;

public class RecurringFailureDetectionServiceTests
{
    [Fact]
    public void Constructor_ThrowsException_IfServicesAreNull()
    {
        // Arrange
        var tempDir = CreateTempDir();
        var historyService = new FailureHistoryService(tempDir);
        var locationService = new LocationHistoryService(tempDir);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RecurringFailureDetectionService(null!, locationService));

        Assert.Throws<ArgumentNullException>(() =>
            new RecurringFailureDetectionService(historyService, null!));
    }

    [Fact]
    public async Task AnalyzeRecurrenceAsync_ReturnsNone_WhenNoHistoryExists()
    {
        // Arrange
        var tempDir = CreateTempDir();
        var historyService = new FailureHistoryService(tempDir);
        var locationService = new LocationHistoryService(tempDir);
        var detectionService = new RecurringFailureDetectionService(historyService, locationService);

        var signature = "Unique.Failure.Signature.Never.Seen.Before";

        // Act
        var result = await detectionService.AnalyzeRecurrenceAsync(signature);

        // Assert
        result.IsRecurring.Should().BeFalse();
        // FIX: Corrected enum name from RecurringPattern to RecurrencePattern
        result.DetectedPattern.Should().Be(RecurrencePattern.None);
        result.TotalOccurrences.Should().Be(0);
        result.RecurrenceScore.Should().Be(0.0);
    }

    [Fact]
    public async Task AnalyzeRecurrenceAsync_DetectsPersistentPattern_WithHighFrequency()
    {
        // Arrange
        var tempDir = CreateTempDir();
        var historyService = new FailureHistoryService(tempDir);
        var locationService = new LocationHistoryService(tempDir);
        var detectionService = new RecurringFailureDetectionService(historyService, locationService);

        var signature = "Persistent.Failure.Signature";
        var now = DateTime.UtcNow;

        // Add 5 failures in the last 2 days (High density)
        for (int i = 0; i < 5; i++)
        {
            await historyService.RecordFailureAsync(new FailureRecord
            {
                FailureSignature = signature,
                OccurredAt = now.AddHours(-i * 10), // Spread over 50 hours
                TestName = "Test1",
                FilePath = "File1.cs",
                Message = "Error"
            });
        }

        // Act
        var result = await detectionService.AnalyzeRecurrenceAsync(signature);

        // Assert
        result.IsRecurring.Should().BeTrue();
        result.TotalOccurrences.Should().Be(5);
        // FIX: Corrected enum name
        result.DetectedPattern.Should().Be(RecurrencePattern.Persistent);
        result.RecurrenceScore.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public async Task AnalyzeRecurrenceAsync_HandlesEmptySignature_Gracefully()
    {
        // Arrange
        var tempDir = CreateTempDir();
        var historyService = new FailureHistoryService(tempDir);
        var locationService = new LocationHistoryService(tempDir);
        var detectionService = new RecurringFailureDetectionService(historyService, locationService);

        // Act
        var result = await detectionService.AnalyzeRecurrenceAsync("");

        // Assert
        result.IsRecurring.Should().BeFalse();
        result.AnalysisSummary.Should().Contain("Empty");
    }

    private string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "ISCM_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}