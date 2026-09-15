using ISCM.Application.Analytics;
using ISCM.Application.Interfaces;
using ISCM.Application.Services;
using ISCM.Application.Snapshots;
using ISCM.Domain.Enums;
using Xunit;

namespace ISCM.Tests.Unit.Analytics;

/// <summary>
/// Unit tests for TrendAnalysisService (Phase 16.2 - Trend Analysis & Historical Charts).
/// Uses a lightweight FakeSnapshotRepository to avoid external mocking dependencies.
/// </summary>
public class TrendAnalysisServiceTests
{
    // ── Asset Trend Tests ──

    [Fact]
    public async Task AnalyzeAssetTrendAsync_NoSnapshots_ReturnsInsufficientData()
    {
        // Arrange
        var repo = new FakeSnapshotRepository();
        var service = new TrendAnalysisService(repo);

        // Act
        var result = await service.AnalyzeAssetTrendAsync("HOST-01");

        // Assert
        Assert.Equal(TrendDirection.InsufficientData, result.Direction);
        Assert.Empty(result.DataPoints);
        Assert.Equal(0, result.TotalScans);
        Assert.Equal(0, result.OverallDelta);
    }

    [Fact]
    public async Task AnalyzeAssetTrendAsync_SingleSnapshot_ReturnsInsufficientData()
    {
        // Arrange
        var repo = new FakeSnapshotRepository();
        repo.Add(CreateSnapshot("HOST-01", DateTime.UtcNow.AddDays(-1), 75, "B"));
        var service = new TrendAnalysisService(repo);

        // Act
        var result = await service.AnalyzeAssetTrendAsync("HOST-01");

        // Assert
        Assert.Equal(TrendDirection.InsufficientData, result.Direction);
        Assert.Single(result.DataPoints);
        Assert.Equal(75, result.AverageScore);
    }

    [Fact]
    public async Task AnalyzeAssetTrendAsync_ImprovingTrend_ReturnsImproving()
    {
        // Arrange
        var repo = new FakeSnapshotRepository();
        var baseDate = DateTime.UtcNow.AddDays(-5);
        repo.Add(CreateSnapshot("HOST-01", baseDate, 50, "D"));
        repo.Add(CreateSnapshot("HOST-01", baseDate.AddDays(1), 60, "C"));
        repo.Add(CreateSnapshot("HOST-01", baseDate.AddDays(2), 70, "B"));
        repo.Add(CreateSnapshot("HOST-01", baseDate.AddDays(3), 80, "A"));
        repo.Add(CreateSnapshot("HOST-01", baseDate.AddDays(4), 90, "A+"));
        var service = new TrendAnalysisService(repo);

        // Act
        var result = await service.AnalyzeAssetTrendAsync("HOST-01");

        // Assert
        Assert.Equal(TrendDirection.Improving, result.Direction);
        Assert.Equal(5, result.TotalScans);
        Assert.Equal(40, result.OverallDelta); // 90 - 50
        Assert.Equal(50, result.MinScore);
        Assert.Equal(90, result.MaxScore);
        Assert.Equal(70, result.AverageScore);
    }

    [Fact]
    public async Task AnalyzeAssetTrendAsync_RegressingTrend_ReturnsRegressing()
    {
        // Arrange
        var repo = new FakeSnapshotRepository();
        var baseDate = DateTime.UtcNow.AddDays(-4);
        repo.Add(CreateSnapshot("HOST-01", baseDate, 90, "A+"));
        repo.Add(CreateSnapshot("HOST-01", baseDate.AddDays(1), 75, "B"));
        repo.Add(CreateSnapshot("HOST-01", baseDate.AddDays(2), 60, "C"));
        repo.Add(CreateSnapshot("HOST-01", baseDate.AddDays(3), 50, "D"));
        var service = new TrendAnalysisService(repo);

        // Act
        var result = await service.AnalyzeAssetTrendAsync("HOST-01");

        // Assert
        Assert.Equal(TrendDirection.Regressing, result.Direction);
        Assert.Equal(-40, result.OverallDelta); // 50 - 90
    }

    [Fact]
    public async Task AnalyzeAssetTrendAsync_StableTrend_ReturnsStable()
    {
        // Arrange
        var repo = new FakeSnapshotRepository();
        var baseDate = DateTime.UtcNow.AddDays(-3);
        repo.Add(CreateSnapshot("HOST-01", baseDate, 72, "B"));
        repo.Add(CreateSnapshot("HOST-01", baseDate.AddDays(1), 74, "B"));
        repo.Add(CreateSnapshot("HOST-01", baseDate.AddDays(2), 73, "B"));
        var service = new TrendAnalysisService(repo);

        // Act
        var result = await service.AnalyzeAssetTrendAsync("HOST-01");

        // Assert
        Assert.Equal(TrendDirection.Stable, result.Direction);
        Assert.Equal(1, result.OverallDelta); // Within ±3 threshold
    }

    [Fact]
    public async Task AnalyzeAssetTrendAsync_DataPointsAreChronologicalAndContainDeltas()
    {
        // Arrange
        var repo = new FakeSnapshotRepository();
        var baseDate = DateTime.UtcNow.AddDays(-3);
        repo.Add(CreateSnapshot("HOST-01", baseDate, 50, "D"));
        repo.Add(CreateSnapshot("HOST-01", baseDate.AddDays(1), 70, "B"));
        repo.Add(CreateSnapshot("HOST-01", baseDate.AddDays(2), 85, "A"));
        var service = new TrendAnalysisService(repo);

        // Act
        var result = await service.AnalyzeAssetTrendAsync("HOST-01");

        // Assert
        Assert.Equal(3, result.DataPoints.Count);

        // First point has no delta
        Assert.Null(result.DataPoints[0].ScoreDelta);
        Assert.Equal(50, result.DataPoints[0].ComplianceScore);

        // Second point: +20
        Assert.Equal(20, result.DataPoints[1].ScoreDelta);
        Assert.Equal(70, result.DataPoints[1].ComplianceScore);

        // Third point: +15
        Assert.Equal(15, result.DataPoints[2].ScoreDelta);
        Assert.Equal(85, result.DataPoints[2].ComplianceScore);

        // Chronological order
        Assert.True(result.DataPoints[0].Timestamp < result.DataPoints[1].Timestamp);
        Assert.True(result.DataPoints[1].Timestamp < result.DataPoints[2].Timestamp);
    }

    [Fact]
    public async Task AnalyzeAssetTrendAsync_MaxDataPoints_CapsResults()
    {
        // Arrange
        var repo = new FakeSnapshotRepository();
        var baseDate = DateTime.UtcNow.AddDays(-20);
        for (int i = 0; i < 20; i++)
        {
            repo.Add(CreateSnapshot("HOST-01", baseDate.AddDays(i), 50 + i, "C"));
        }
        var service = new TrendAnalysisService(repo);

        // Act
        var result = await service.AnalyzeAssetTrendAsync("HOST-01", maxDataPoints: 5);

        // Assert
        Assert.Equal(5, result.DataPoints.Count);
        // Should take the LAST 5 (most recent)
        Assert.Equal(65, result.DataPoints[0].ComplianceScore); // i=15
        Assert.Equal(69, result.DataPoints[4].ComplianceScore); // i=19
    }

    [Fact]
    public async Task AnalyzeAssetTrendAsync_EmptyHostname_ThrowsArgumentException()
    {
        // Arrange
        var repo = new FakeSnapshotRepository();
        var service = new TrendAnalysisService(repo);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.AnalyzeAssetTrendAsync(string.Empty));
    }

    [Fact]
    public async Task AnalyzeAssetTrendAsync_NullRepository_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new TrendAnalysisService(null!));
    }

    // ── Global Trend Tests ──

    [Fact]
    public async Task AnalyzeGlobalTrendAsync_NoSnapshots_ReturnsInsufficientData()
    {
        // Arrange
        var repo = new FakeSnapshotRepository();
        var service = new TrendAnalysisService(repo);

        // Act
        var result = await service.AnalyzeGlobalTrendAsync();

        // Assert
        Assert.Equal(TrendDirection.InsufficientData, result.Direction);
        Assert.Empty(result.DataPoints);
    }

    [Fact]
    public async Task AnalyzeGlobalTrendAsync_MultipleHosts_ReturnsAggregatedTrend()
    {
        // Arrange
        var repo = new FakeSnapshotRepository();
        var baseDate = DateTime.UtcNow.AddDays(-3);

        // HOST-01 snapshots
        repo.Add(CreateSnapshot("HOST-01", baseDate, 70, "B"));
        repo.Add(CreateSnapshot("HOST-01", baseDate.AddDays(1), 75, "B"));
        repo.Add(CreateSnapshot("HOST-01", baseDate.AddDays(2), 80, "A"));

        // HOST-02 snapshots
        repo.Add(CreateSnapshot("HOST-02", baseDate, 60, "C"));
        repo.Add(CreateSnapshot("HOST-02", baseDate.AddDays(1), 65, "C"));

        var service = new TrendAnalysisService(repo);

        // Act
        var result = await service.AnalyzeGlobalTrendAsync();

        // Assert - Should aggregate across hosts
        Assert.True(result.TotalScans >= 2);
    }

    // ── Helper Methods ──

    private static ScanSnapshot CreateSnapshot(string hostname, DateTime completedAt, int score, string grade)
    {
        var snapshot = new ScanSnapshot
        {
            SnapshotId = Guid.NewGuid(),
            ScanId = Guid.NewGuid().ToString("N"),
            AssetId = hostname,
            Hostname = hostname,
            IpAddress = "192.168.1.100",
            MacAddress = "00:11:22:33:44:55",
            OsVersion = "Windows Server 2022",
            OsBuild = 20348,
            ScanMode = ScanMode.Full,
            StartedAtUtc = completedAt.AddMinutes(-5),
            CompletedAtUtc = completedAt,
            OverallStatus = score >= 70 ? CheckStatus.Pass : CheckStatus.Fail,
            ComplianceScore = score,
            Grade = grade,
            PassCount = score,
            FailCount = 100 - score,
            WarningCount = 0,
            ErrorCount = 0,
            TotalControlCount = 100,
            TotalSubControlCount = 100,
            Controls = new List<ControlSnapshot>(),
            Findings = new List<FindingSnapshot>(),
            IntegrityHash = "abc123",
            SchemaVersion = 1
        };
        return snapshot;
    }

    // ── Fake Repository ──

    private sealed class FakeSnapshotRepository : ISnapshotRepository
    {
        private readonly List<ScanSnapshot> _snapshots = new();

        public void Add(ScanSnapshot snapshot) => _snapshots.Add(snapshot);

        public Task SaveAsync(ScanSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            _snapshots.Add(snapshot);
            return Task.CompletedTask;
        }

        public Task<ScanSnapshot?> GetAsync(Guid snapshotId, CancellationToken cancellationToken = default)
            => Task.FromResult(_snapshots.FirstOrDefault(s => s.SnapshotId == snapshotId));

        public Task<ScanSnapshot?> GetByScanIdAsync(string scanId, CancellationToken cancellationToken = default)
            => Task.FromResult(_snapshots.FirstOrDefault(s => s.ScanId == scanId));

        public Task<IReadOnlyList<ScanSnapshot>> GetByAssetAsync(string hostname, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ScanSnapshot> result = _snapshots
                .Where(s => string.Equals(s.Hostname, hostname, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.CompletedAtUtc)
                .ToList();
            return Task.FromResult(result);
        }

        public Task<ScanSnapshot?> GetLatestByAssetAsync(string hostname, CancellationToken cancellationToken = default)
        {
            var latest = _snapshots
                .Where(s => string.Equals(s.Hostname, hostname, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.CompletedAtUtc)
                .FirstOrDefault();
            return Task.FromResult(latest);
        }

        public Task<IReadOnlyList<ScanSnapshot>> GetRecentAsync(int limit = 10, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ScanSnapshot> result = _snapshots
                .OrderByDescending(s => s.CompletedAtUtc)
                .Take(limit)
                .ToList();
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<SnapshotSummary>> ListSummariesAsync(string? hostname = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SnapshotSummary>>(Array.Empty<SnapshotSummary>());

        public Task<bool> DeleteAsync(Guid snapshotId, CancellationToken cancellationToken = default)
            => Task.FromResult(_snapshots.RemoveAll(s => s.SnapshotId == snapshotId) > 0);

        public Task<int> CountAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_snapshots.Count);

        public Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> ExistsAsync(Guid snapshotId, CancellationToken cancellationToken = default)
            => Task.FromResult(_snapshots.Any(s => s.SnapshotId == snapshotId));
    }
}