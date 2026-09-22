using System;
using System.IO;
using System.Threading.Tasks;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class LocationHistoryServiceTests : IDisposable
{
    private readonly string _testTempDir;
    private readonly LocationHistoryService _service;

    public LocationHistoryServiceTests()
    {
        _testTempDir = Path.Combine(Path.GetTempPath(), $"ISCM_LocHist_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testTempDir);
        _service = new LocationHistoryService(_testTempDir);
    }

    [Fact]
    public void Constructor_CreatesHistoryDirectory_IfMissing()
    {
        var historyDir = Path.Combine(_testTempDir, ".bugfinder", "history");
        Directory.Exists(historyDir).Should().BeTrue();

        var filePath = Path.Combine(historyDir, "location_history.json");
        File.Exists(filePath).Should().BeTrue();
    }

    [Fact]
    public async Task RecordLocationFailureAsync_SavesRecordToDisk()
    {
        // Arrange
        var record = new CodeLocationRecord
        {
            FilePath = "src/MyClass.cs",
            MethodName = "DoWork",
            LineNumber = 42,
            FailureSignature = "FAIL_001",
            RecordedAt = DateTime.UtcNow,
            CommitSha = "abc123"
        };

        // Act
        await _service.RecordLocationFailureAsync(record);

        // Assert
        var result = await _service.GetLocationHistoryAsync("src/MyClass.cs", "DoWork", 42);
        result.TotalFailuresAtLocation.Should().Be(1);
        result.RecentRecords.Count.Should().Be(1);
    }

    [Fact]
    public async Task GetLocationHistoryAsync_ReturnsEmpty_WhenNoRecordsExist()
    {
        // Act
        var result = await _service.GetLocationHistoryAsync("NonExistent.cs");

        // Assert
        result.TotalFailuresAtLocation.Should().Be(0);
        result.RecentRecords.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHotspotsAsync_ReturnsLocationsAboveThreshold()
    {
        // Arrange: Add 4 failures to same location
        for (int i = 0; i < 4; i++)
        {
            await _service.RecordLocationFailureAsync(new CodeLocationRecord
            {
                FilePath = "Hotspot.cs",
                MethodName = "BadMethod",
                LineNumber = 10,
                FailureSignature = $"FAIL_{i}",
                RecordedAt = DateTime.UtcNow.AddMinutes(-i)
            });
        }

        // Add 1 failure to another location (below threshold)
        await _service.RecordLocationFailureAsync(new CodeLocationRecord
        {
            FilePath = "Coldspot.cs",
            MethodName = "GoodMethod",
            FailureSignature = "FAIL_RARE"
        });

        // Act
        var hotspots = await _service.GetHotspotsAsync(threshold: 3);

        // Assert
        hotspots.Count.Should().Be(1);
        hotspots[0].FilePath.Should().Be("Hotspot.cs");
        hotspots[0].TotalFailuresAtLocation.Should().Be(4);
        hotspots[0].IsHotspot.Should().BeTrue();
    }

    [Fact]
    public void LocationHistoryResult_IsHotspot_CalculatedCorrectly()
    {
        // Arrange
        var resultBelow = new LocationHistoryResult { TotalFailuresAtLocation = 2 };
        var resultAbove = new LocationHistoryResult { TotalFailuresAtLocation = 3 };

        // Assert
        resultBelow.IsHotspot.Should().BeFalse();
        resultAbove.IsHotspot.Should().BeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testTempDir))
        {
            Directory.Delete(_testTempDir, recursive: true);
        }
    }
}