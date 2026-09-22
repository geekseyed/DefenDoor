using System;
using System.IO;
using System.Threading.Tasks;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class FailureHistoryServiceTests
{
    private readonly string _testTempDir;

    public FailureHistoryServiceTests()
    {
        // Create a unique temp directory for each test run to avoid collision
        _testTempDir = Path.Combine(Path.GetTempPath(), "ISCM_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testTempDir);
    }

    [Fact]
    public void Constructor_CreatesHistoryDirectory_IfMissing()
    {
        // Arrange
        var historyPath = Path.Combine(_testTempDir, ".bugfinder", "history");
        Directory.Delete(historyPath, true); // Ensure it's gone

        // Act
        _ = new FailureHistoryService(_testTempDir);

        // Assert
        Directory.Exists(historyPath).Should().BeTrue();
        File.Exists(Path.Combine(historyPath, "failure_history.json")).Should().BeTrue();
    }

    [Fact]
    public async Task RecordFailureAsync_SavesRecordToDisk()
    {
        // Arrange
        var service = new FailureHistoryService(_testTempDir);
        var record = new FailureRecord
        {
            FailureSignature = "SIG_TEST_001",
            TestName = "MyTestClass.MyTestMethod",
            Summary = "Assertion failed",
            OccurredAt = DateTime.UtcNow
        };

        // Act
        await service.RecordFailureAsync(record);

        // Assert
        var history = await service.GetHistoryAsync("SIG_TEST_001");
        history.TotalOccurrences.Should().Be(1);
        history.RecentOccurrences.Should().ContainSingle(r => r.TestName == "MyTestClass.MyTestMethod");
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsEmpty_WhenNoMatches()
    {
        // Arrange
        var service = new FailureHistoryService(_testTempDir);

        // Act
        var history = await service.GetHistoryAsync("NON_EXISTENT_SIG");

        // Assert
        history.TotalOccurrences.Should().Be(0);
        history.IsRecurring.Should().BeFalse();
    }

    [Fact]
    public async Task IsRecurringFailureAsync_ReturnsTrue_WhenMultipleOccurrencesExist()
    {
        // Arrange
        var service = new FailureHistoryService(_testTempDir);
        var sig = "SIG_RECURRING_TEST";

        await service.RecordFailureAsync(new FailureRecord { FailureSignature = sig, OccurredAt = DateTime.UtcNow });
        await Task.Delay(10); // Small delay to ensure distinct timestamps
        await service.RecordFailureAsync(new FailureRecord { FailureSignature = sig, OccurredAt = DateTime.UtcNow });

        // Act
        var isRecurring = await service.IsRecurringFailureAsync(sig);

        // Assert
        isRecurring.Should().BeTrue();
    }

    [Fact]
    public async Task FailureHistoryResult_Model_HasCorrectProperties()
    {
        // Arrange
        var result = new FailureHistoryResult();

        // Assert
        result.RecentOccurrences.Should().NotBeNull();
        result.IsRecurring.Should().BeFalse(); // Default
    }

    // Cleanup temp dir after tests (optional, OS usually handles temp)
    ~FailureHistoryServiceTests()
    {
        try { if (Directory.Exists(_testTempDir)) Directory.Delete(_testTempDir, true); } catch { }
    }
}