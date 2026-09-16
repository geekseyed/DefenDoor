using ISCM.Application.Analytics;
using ISCM.Application.Interfaces;
using ISCM.Application.Reporting;
using ISCM.Application.Services;
using ISCM.Application.Snapshots;
using ISCM.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ISCM.Tests.Unit.Analytics;

/// <summary>
/// Unit tests for ExecutiveKpiService (Phase 16.5 - Executive Dashboard & KPIs).
/// Validates KPI calculations from historical snapshot data.
/// </summary>
public class ExecutiveKpiServiceTests
{
    private readonly FakeSnapshotRepository _repo;
    private readonly FakeTrendAnalysisService _trendService;
    private readonly FakeScheduledReportService _scheduleService;
    private readonly ExecutiveKpiService _service;

    public ExecutiveKpiServiceTests()
    {
        _repo = new FakeSnapshotRepository();
        _trendService = new FakeTrendAnalysisService();
        _scheduleService = new FakeScheduledReportService();
        var logger = NullLogger<ExecutiveKpiService>.Instance;
        _service = new ExecutiveKpiService(_repo, _trendService, _scheduleService, logger);
    }

    // ── Empty State Tests ──

    [Fact]
    public async Task CalculateKpisAsync_EmptyRepository_ReturnsEmptyKpi()
    {
        // Act
        var kpi = await _service.CalculateKpisAsync();

        // Assert
        Assert.Equal(0, kpi.CurrentScore);
        Assert.Equal("N/A", kpi.CurrentGrade);
        Assert.Equal(0, kpi.TotalChecks);
        Assert.Equal(0, kpi.TotalScansAllTime);
        Assert.Equal(TrendDirection.InsufficientData, kpi.Trend30Day);
    }

    // ── Single Snapshot Tests ──

    [Fact]
    public async Task CalculateKpisAsync_SingleSnapshot_ReturnsBasicKpis()
    {
        // Arrange
        var snapshot = CreateSnapshot(score: 75, grade: "B", daysAgo: 2);
        _repo.Snapshots.Add(snapshot);

        // Act
        var kpi = await _service.CalculateKpisAsync();

        // Assert
        Assert.Equal(75, kpi.CurrentScore);
        Assert.Equal("B", kpi.CurrentGrade);
        Assert.Equal(1, kpi.TotalScansAllTime);
        Assert.Equal(2, kpi.DaysSinceLastScan);
    }

    [Fact]
    public async Task CalculateKpisAsync_SingleSnapshot_InsufficientDataForTrend()
    {
        // Arrange
        _repo.Snapshots.Add(CreateSnapshot(score: 80, grade: "B", daysAgo: 5));

        // Act
        var kpi = await _service.CalculateKpisAsync();

        // Assert
        Assert.Equal(TrendDirection.InsufficientData, kpi.Trend30Day);
        Assert.Equal(TrendDirection.InsufficientData, kpi.Trend90Day);
    }

    // ── Multiple Snapshots Tests ──

    [Fact]
    public async Task CalculateKpisAsync_MultipleSnapshots_CalculatesCorrectAverages()
    {
        // Arrange
        _repo.Snapshots.Add(CreateSnapshot(score: 70, grade: "B", daysAgo: 10));
        _repo.Snapshots.Add(CreateSnapshot(score: 80, grade: "B", daysAgo: 5));
        _repo.Snapshots.Add(CreateSnapshot(score: 90, grade: "A", daysAgo: 1));

        // Act
        var kpi = await _service.CalculateKpisAsync();

        // Assert - Average of last 30 days (all 3 snapshots)
        Assert.Equal(80, kpi.AverageScoreLast30Days); // (70+80+90)/3 = 80
        Assert.Equal(3, kpi.TotalScansLast30Days);
        Assert.Equal(90, kpi.CurrentScore); // Most recent
    }

    [Fact]
    public async Task CalculateKpisAsync_SnapshotsBeyond90Days_ExcludedFrom90DayAverage()
    {
        // Arrange
        _repo.Snapshots.Add(CreateSnapshot(score: 50, grade: "D", daysAgo: 100)); // Beyond 90 days
        _repo.Snapshots.Add(CreateSnapshot(score: 70, grade: "B", daysAgo: 50));
        _repo.Snapshots.Add(CreateSnapshot(score: 90, grade: "A", daysAgo: 10));

        // Act
        var kpi = await _service.CalculateKpisAsync();

        // Assert
        Assert.Equal(3, kpi.TotalScansAllTime);
        Assert.Equal(2, kpi.TotalScansLast90Days);
        Assert.Equal(80, kpi.AverageScoreLast90Days); // (70+90)/2 = 80
    }

    // ── Threshold Tests ──

    [Fact]
    public async Task CalculateKpisAsync_ScoreAbove70_MeetsMinimumThreshold()
    {
        // Arrange
        _repo.Snapshots.Add(CreateSnapshot(score: 75, grade: "B", daysAgo: 1));

        // Act
        var kpi = await _service.CalculateKpisAsync();

        // Assert
        Assert.True(kpi.MeetsMinimumThreshold);
        Assert.False(kpi.MeetsTargetThreshold);
    }

    [Fact]
    public async Task CalculateKpisAsync_ScoreAbove85_MeetsTargetThreshold()
    {
        // Arrange
        _repo.Snapshots.Add(CreateSnapshot(score: 90, grade: "A", daysAgo: 1));

        // Act
        var kpi = await _service.CalculateKpisAsync();

        // Assert
        Assert.True(kpi.MeetsMinimumThreshold);
        Assert.True(kpi.MeetsTargetThreshold);
    }

    // ── Risk Indicator Tests ──

    [Fact]
    public async Task CalculateKpisAsync_CountsFailuresBySeverity()
    {
        // Arrange
        var findings = new List<FindingSnapshot>
        {
            CreateFinding("CHK-1", CheckStatus.Fail, CheckSeverity.Critical),
            CreateFinding("CHK-2", CheckStatus.Fail, CheckSeverity.Critical),
            CreateFinding("CHK-3", CheckStatus.Fail, CheckSeverity.High),
            CreateFinding("CHK-4", CheckStatus.Fail, CheckSeverity.Medium),
            CreateFinding("CHK-5", CheckStatus.Fail, CheckSeverity.Low),
            CreateFinding("CHK-6", CheckStatus.Pass, CheckSeverity.Medium)
        };
        var snapshot = CreateSnapshotWithFindings(score: 50, grade: "D", daysAgo: 1, findings);
        _repo.Snapshots.Add(snapshot);

        // Act
        var kpi = await _service.CalculateKpisAsync();

        // Assert
        Assert.Equal(2, kpi.CriticalFailures);
        Assert.Equal(1, kpi.HighFailures);
        Assert.Equal(1, kpi.MediumFailures);
        Assert.Equal(1, kpi.LowFailures);
    }

    [Fact]
    public async Task CalculateKpisAsync_IdentifiesTopFailingControl()
    {
        // Arrange
        var findings = new List<FindingSnapshot>
        {
            CreateFinding("RDP-001", CheckStatus.Fail, CheckSeverity.High),
            CreateFinding("RDP-001", CheckStatus.Fail, CheckSeverity.High),
            CreateFinding("RDP-001", CheckStatus.Fail, CheckSeverity.High),
            CreateFinding("UAC-001", CheckStatus.Fail, CheckSeverity.Medium),
            CreateFinding("UAC-001", CheckStatus.Fail, CheckSeverity.Medium),
            CreateFinding("FW-001", CheckStatus.Fail, CheckSeverity.Low)
        };
        var snapshot = CreateSnapshotWithFindings(score: 50, grade: "D", daysAgo: 1, findings);
        _repo.Snapshots.Add(snapshot);

        // Act
        var kpi = await _service.CalculateKpisAsync();

        // Assert
        Assert.Equal("RDP-001", kpi.TopFailingControl);
        Assert.Equal(3, kpi.TopFailingControlCount);
    }

    // ── Streak Tests ──

    [Fact]
    public async Task CalculateKpisAsync_CalculatesStreaks()
    {
        // Arrange: 5 scans, 3 consecutive improvements
        _repo.Snapshots.Add(CreateSnapshot(score: 50, grade: "D", daysAgo: 10));
        _repo.Snapshots.Add(CreateSnapshot(score: 60, grade: "D", daysAgo: 8)); // +10
        _repo.Snapshots.Add(CreateSnapshot(score: 65, grade: "D", daysAgo: 6)); // +5
        _repo.Snapshots.Add(CreateSnapshot(score: 70, grade: "B", daysAgo: 4)); // +5
        _repo.Snapshots.Add(CreateSnapshot(score: 68, grade: "D", daysAgo: 2)); // -2 (break)

        // Act
        var kpi = await _service.CalculateKpisAsync();

        // Assert - longest streak without regression is 3 (50→60→65→70)
        Assert.Equal(3, kpi.LongestStreakWithoutRegression);
        Assert.Equal(0, kpi.ConsecutiveImprovements); // Last one broke the streak
    }

    [Fact]
    public async Task CalculateKpisAsync_ConsecutiveImprovements_CountsFromEnd()
    {
        // Arrange: Ends with 3 improvements
        _repo.Snapshots.Add(CreateSnapshot(score: 50, grade: "D", daysAgo: 10));
        _repo.Snapshots.Add(CreateSnapshot(score: 60, grade: "D", daysAgo: 8));
        _repo.Snapshots.Add(CreateSnapshot(score: 70, grade: "B", daysAgo: 6));
        _repo.Snapshots.Add(CreateSnapshot(score: 80, grade: "B", daysAgo: 4));
        _repo.Snapshots.Add(CreateSnapshot(score: 90, grade: "A", daysAgo: 2));

        // Act
        var kpi = await _service.CalculateKpisAsync();

        // Assert
        Assert.Equal(4, kpi.ConsecutiveImprovements); // 4 improvements in a row at end
    }

    // ── Trend Service Integration ──

    [Fact]
    public async Task CalculateKpisAsync_IntegratesTrendService_Improving()
    {
        // Arrange
        _repo.Snapshots.Add(CreateSnapshot(score: 70, grade: "B", daysAgo: 10));
        _repo.Snapshots.Add(CreateSnapshot(score: 90, grade: "A", daysAgo: 1));
        _trendService.GlobalTrendDirection = TrendDirection.Improving;

        // Act
        var kpi = await _service.CalculateKpisAsync();

        // Assert
        Assert.Equal(TrendDirection.Improving, kpi.Trend30Day);
    }

    [Fact]
    public async Task CalculateKpisAsync_IntegratesTrendService_Regressing()
    {
        // Arrange
        _repo.Snapshots.Add(CreateSnapshot(score: 90, grade: "A", daysAgo: 10));
        _repo.Snapshots.Add(CreateSnapshot(score: 70, grade: "B", daysAgo: 1));
        _trendService.GlobalTrendDirection = TrendDirection.Regressing;

        // Act
        var kpi = await _service.CalculateKpisAsync();

        // Assert
        Assert.Equal(TrendDirection.Regressing, kpi.Trend30Day);
    }

    // ── Schedule Integration ──

    [Fact]
    public async Task CalculateKpisAsync_CalculatesDaysUntilNextScan()
    {
        // Arrange
        _repo.Snapshots.Add(CreateSnapshot(score: 80, grade: "B", daysAgo: 1));
        _scheduleService.Schedules.Add(new ReportSchedule
        {
            IsActive = true,
            NextExecutionAtUtc = DateTime.UtcNow.AddDays(5)
        });

        // Act
        var kpi = await _service.CalculateKpisAsync();

        // Assert
        Assert.Equal(5, kpi.DaysUntilNextScheduledScan);
    }

    [Fact]
    public async Task CalculateKpisAsync_NoActiveSchedules_ReturnsZero()
    {
        // Arrange
        _repo.Snapshots.Add(CreateSnapshot(score: 80, grade: "B", daysAgo: 1));

        // Act
        var kpi = await _service.CalculateKpisAsync();

        // Assert
        Assert.Equal(0, kpi.DaysUntilNextScheduledScan);
    }

    // ── Hostname Filter Tests ──

    [Fact]
    public async Task CalculateKpisAsync_HostnameFilter_OnlyIncludesMatchingSnapshots()
    {
        // Arrange
        _repo.Snapshots.Add(CreateSnapshot(score: 60, grade: "D", daysAgo: 5, hostname: "SERVER-A"));
        _repo.Snapshots.Add(CreateSnapshot(score: 90, grade: "A", daysAgo: 2, hostname: "SERVER-B"));
        _repo.Snapshots.Add(CreateSnapshot(score: 80, grade: "B", daysAgo: 1, hostname: "SERVER-A"));

        // Act
        var kpi = await _service.CalculateKpisAsync(hostname: "SERVER-A");

        // Assert - Should only see SERVER-A snapshots
        Assert.Equal(80, kpi.CurrentScore);
        Assert.Equal(2, kpi.TotalScansAllTime);
        Assert.Equal(70, kpi.AverageScoreLast30Days); // (60+80)/2
    }

    // ── Helper Methods ──

    private static ScanSnapshot CreateSnapshot(
        int score,
        string grade,
        int daysAgo,
        string hostname = "TEST-HOST")
    {
        return new ScanSnapshot
        {
            SnapshotId = Guid.NewGuid(),
            ScanId = Guid.NewGuid().ToString("N"),
            AssetId = "asset-1",
            Hostname = hostname,
            IpAddress = "192.168.1.1",
            MacAddress = "00:11:22:33:44:55",
            OsVersion = "Windows 11",
            OsBuild = 22631,
            CompletedAtUtc = DateTime.UtcNow.AddDays(-daysAgo),
            StartedAtUtc = DateTime.UtcNow.AddDays(-daysAgo).AddMinutes(-5),
            ComplianceScore = score,
            Grade = grade,
            PassCount = score,
            FailCount = 100 - score,
            TotalControlCount = 100,
            TotalSubControlCount = 200,
            Findings = new List<FindingSnapshot>(),
            Controls = new List<ControlSnapshot>()
        };
    }

    private static ScanSnapshot CreateSnapshotWithFindings(
        int score,
        string grade,
        int daysAgo,
        List<FindingSnapshot> findings,
        string hostname = "TEST-HOST")
    {
        return new ScanSnapshot
        {
            SnapshotId = Guid.NewGuid(),
            ScanId = Guid.NewGuid().ToString("N"),
            AssetId = "asset-1",
            Hostname = hostname,
            IpAddress = "192.168.1.1",
            MacAddress = "00:11:22:33:44:55",
            OsVersion = "Windows 11",
            OsBuild = 22631,
            CompletedAtUtc = DateTime.UtcNow.AddDays(-daysAgo),
            StartedAtUtc = DateTime.UtcNow.AddDays(-daysAgo).AddMinutes(-5),
            ComplianceScore = score,
            Grade = grade,
            PassCount = score,
            FailCount = 100 - score,
            TotalControlCount = 100,
            TotalSubControlCount = 200,
            Findings = findings, // ← استفاده در object initializer
            Controls = new List<ControlSnapshot>()
        };
    }

    private static FindingSnapshot CreateFinding(
        string checkId,
        CheckStatus status,
        CheckSeverity severity)
    {
        return new FindingSnapshot
        {
            CheckId = checkId,
            Name = checkId,
            Status = status,
            Severity = severity,
            Category = default, // ← استفاده از default به جای CheckCategory.Security
            CurrentValue = "actual",
            ExpectedValue = "expected"
        };
    }

    // ── Fake Implementations ──

    private sealed class FakeSnapshotRepository : ISnapshotRepository
    {
        public List<ScanSnapshot> Snapshots { get; } = new();

        public Task SaveAsync(ScanSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            Snapshots.Add(snapshot);
            return Task.CompletedTask;
        }

        public Task<ScanSnapshot?> GetAsync(Guid snapshotId, CancellationToken cancellationToken = default)
            => Task.FromResult<ScanSnapshot?>(Snapshots.FirstOrDefault(s => s.SnapshotId == snapshotId));

        public Task<ScanSnapshot?> GetByScanIdAsync(string scanId, CancellationToken cancellationToken = default)
            => Task.FromResult<ScanSnapshot?>(Snapshots.FirstOrDefault(s => s.ScanId == scanId));

        public Task<IReadOnlyList<ScanSnapshot>> GetByAssetAsync(string hostname, CancellationToken cancellationToken = default)
        {
            var result = Snapshots
                .Where(s => s.Hostname == hostname)
                .OrderByDescending(s => s.CompletedAtUtc)
                .ToList();
            return Task.FromResult<IReadOnlyList<ScanSnapshot>>(result);
        }

        public Task<ScanSnapshot?> GetLatestByAssetAsync(string hostname, CancellationToken cancellationToken = default)
        {
            var result = Snapshots
                .Where(s => s.Hostname == hostname)
                .OrderByDescending(s => s.CompletedAtUtc)
                .FirstOrDefault();
            return Task.FromResult<ScanSnapshot?>(result);
        }

        public Task<IReadOnlyList<ScanSnapshot>> GetRecentAsync(int limit = 10, CancellationToken cancellationToken = default)
        {
            var result = Snapshots
                .OrderByDescending(s => s.CompletedAtUtc)
                .Take(limit)
                .ToList();
            return Task.FromResult<IReadOnlyList<ScanSnapshot>>(result);
        }

        public Task<IReadOnlyList<SnapshotSummary>> ListSummariesAsync(string? hostname = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SnapshotSummary>>(Array.Empty<SnapshotSummary>());

        public Task<bool> DeleteAsync(Guid snapshotId, CancellationToken cancellationToken = default)
        {
            var snapshot = Snapshots.FirstOrDefault(s => s.SnapshotId == snapshotId);
            if (snapshot == null) return Task.FromResult(false);
            Snapshots.Remove(snapshot);
            return Task.FromResult(true);
        }

        public Task<int> CountAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Snapshots.Count);

        public Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> ExistsAsync(Guid snapshotId, CancellationToken cancellationToken = default)
            => Task.FromResult(Snapshots.Any(s => s.SnapshotId == snapshotId));
    }

    private sealed class FakeTrendAnalysisService : ITrendAnalysisService
    {
        public TrendDirection GlobalTrendDirection { get; set; } = TrendDirection.Stable;

        public Task<TrendAnalysisResult> AnalyzeAssetTrendAsync(string hostname, int maxDataPoints = 50, CancellationToken cancellationToken = default)
            => Task.FromResult(new TrendAnalysisResult { Direction = GlobalTrendDirection });

        public Task<TrendAnalysisResult> AnalyzeGlobalTrendAsync(int maxDataPoints = 50, CancellationToken cancellationToken = default)
            => Task.FromResult(new TrendAnalysisResult { Direction = GlobalTrendDirection });
    }

    private sealed class FakeScheduledReportService : IScheduledReportService
    {
        public List<ReportSchedule> Schedules { get; } = new();

        public Task<IReadOnlyList<ReportSchedule>> ListSchedulesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ReportSchedule>>(Schedules);

        public Task<ReportSchedule?> GetScheduleAsync(string scheduleId, CancellationToken cancellationToken = default)
            => Task.FromResult<ReportSchedule?>(Schedules.FirstOrDefault(s => s.ScheduleId == scheduleId));

        public Task<ReportSchedule> SaveScheduleAsync(ReportSchedule schedule, CancellationToken cancellationToken = default)
        {
            Schedules.Add(schedule);
            return Task.FromResult(schedule);
        }

        public Task<bool> DeleteScheduleAsync(string scheduleId, CancellationToken cancellationToken = default)
        {
            var s = Schedules.FirstOrDefault(x => x.ScheduleId == scheduleId);
            if (s == null) return Task.FromResult(false);
            Schedules.Remove(s);
            return Task.FromResult(true);
        }

        public Task<string?> ExecuteNowAsync(string scheduleId, string triggeredBy = "User", CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<IReadOnlyList<ScheduleExecutionLog>> ProcessDueSchedulesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ScheduleExecutionLog>>(Array.Empty<ScheduleExecutionLog>());

        public Task<IReadOnlyList<ScheduleExecutionLog>> GetExecutionHistoryAsync(int limit = 50, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ScheduleExecutionLog>>(Array.Empty<ScheduleExecutionLog>());
    }
}