using ISCM.Application.Interfaces;
using ISCM.Application.Reporting;
using ISCM.Application.Services;
using ISCM.Application.Snapshots;
using ISCM.Domain.Entities;
using ISCM.Domain.Enums;
using ISCM.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ISCM.Tests.Unit.Reporting;

/// <summary>
/// Unit tests for ScheduledReportService (Phase 16.4 - Scheduled Reports).
/// Tests CRUD operations, schedule calculation, and Air-Gapped execution modes.
/// </summary>
public class ScheduledReportServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ScheduledReportService _service;
    private readonly FakeScanService _fakeScanService;
    private readonly FakeReportService _fakeReportService;
    private readonly FakeReportTemplateService _fakeTemplateService;

    public ScheduledReportServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ISCM_ScheduleTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var fakeDbPath = Path.Combine(_tempDir, "defendoor.db");
        var pathProvider = new FakeStoragePathProvider(fakeDbPath);
        var logger = NullLogger<ScheduledReportService>.Instance;

        _fakeScanService = new FakeScanService();
        _fakeReportService = new FakeReportService();
        _fakeTemplateService = new FakeReportTemplateService();
        var fakeSnapshotRepo = new FakeSnapshotRepository();

        _service = new ScheduledReportService(
            pathProvider,
            _fakeReportService,
            _fakeTemplateService,
            _fakeScanService,
            fakeSnapshotRepo,
            logger);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { /* Ignore cleanup errors */ }
        }
    }

    // ── CRUD Tests ──

    [Fact]
    public async Task ListSchedulesAsync_EmptyDirectory_ReturnsEmptyList()
    {
        // Act
        var schedules = await _service.ListSchedulesAsync();

        // Assert
        Assert.Empty(schedules);
    }

    [Fact]
    public async Task SaveAndGetScheduleAsync_PersistsAndRetrieves()
    {
        // Arrange
        var schedule = CreateTestSchedule("Weekly Audit");

        // Act
        await _service.SaveScheduleAsync(schedule);
        var loaded = await _service.GetScheduleAsync(schedule.ScheduleId);

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal("Weekly Audit", loaded!.Name);
        Assert.Equal(ScheduleFrequency.Weekly, loaded.Frequency);
    }

    [Fact]
    public async Task SaveScheduleAsync_CalculatesNextExecutionAutomatically()
    {
        // Arrange
        var schedule = new ReportSchedule
        {
            Name = "Daily Scan",
            Frequency = ScheduleFrequency.Daily,
            IntervalValue = 1,
            ExecutionTime = new TimeSpan(10, 0, 0),
            AutoExecute = false // Air-Gapped: requires user approval
        };

        // Act
        var saved = await _service.SaveScheduleAsync(schedule);

        // Assert
        Assert.NotNull(saved.NextExecutionAtUtc);
        Assert.True(saved.NextExecutionAtUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task DeleteScheduleAsync_RemovesSchedule()
    {
        // Arrange
        var schedule = CreateTestSchedule("To Delete");
        await _service.SaveScheduleAsync(schedule);

        // Act
        var deleted = await _service.DeleteScheduleAsync(schedule.ScheduleId);
        var loaded = await _service.GetScheduleAsync(schedule.ScheduleId);

        // Assert
        Assert.True(deleted);
        Assert.Null(loaded);
    }

    [Fact]
    public async Task DeleteScheduleAsync_NonExistent_ReturnsFalse()
    {
        // Act
        var deleted = await _service.DeleteScheduleAsync("non-existent-id");

        // Assert
        Assert.False(deleted);
    }

    // ── Schedule Calculation Tests ──

    [Fact]
    public void CalculateNextExecution_Hourly_AddsHours()
    {
        // Arrange
        var schedule = new ReportSchedule
        {
            Frequency = ScheduleFrequency.Hourly,
            IntervalValue = 2
        };
        var from = new DateTime(2024, 1, 1, 10, 0, 0);

        // Act
        var next = schedule.CalculateNextExecution(from);

        // Assert
        Assert.Equal(new DateTime(2024, 1, 1, 12, 0, 0), next);
    }

    [Fact]
    public void CalculateNextExecution_Daily_AddsDays()
    {
        // Arrange
        var schedule = new ReportSchedule
        {
            Frequency = ScheduleFrequency.Daily,
            IntervalValue = 3
        };
        var from = new DateTime(2024, 1, 1, 10, 0, 0);

        // Act
        var next = schedule.CalculateNextExecution(from);

        // Assert
        Assert.Equal(new DateTime(2024, 1, 4, 10, 0, 0), next);
    }

    [Fact]
    public void CalculateNextExecution_Weekly_TargetsCorrectDay()
    {
        // Arrange
        var schedule = new ReportSchedule
        {
            Frequency = ScheduleFrequency.Weekly,
            TargetDayOfWeek = DayOfWeek.Friday,
            ExecutionTime = new TimeSpan(9, 0, 0)
        };
        // Monday
        var from = new DateTime(2024, 1, 1, 10, 0, 0);

        // Act
        var next = schedule.CalculateNextExecution(from);

        // Assert - Should be next Friday (Jan 5)
        Assert.Equal(DayOfWeek.Friday, next.DayOfWeek);
        Assert.Equal(9, next.Hour);
    }

    [Fact]
    public void CalculateNextExecution_Monthly_TargetsCorrectDay()
    {
        // Arrange
        var schedule = new ReportSchedule
        {
            Frequency = ScheduleFrequency.Monthly,
            DayOfMonth = 15,
            ExecutionTime = new TimeSpan(8, 0, 0)
        };
        var from = new DateTime(2024, 1, 10, 10, 0, 0);

        // Act
        var next = schedule.CalculateNextExecution(from);

        // Assert
        Assert.Equal(15, next.Day);
        Assert.Equal(1, next.Month);
    }

    // ── Air-Gapped Execution Mode Tests ──

    [Fact]
    public async Task ProcessDueSchedulesAsync_AutoExecuteDisabled_SkipsAndLogs()
    {
        // Arrange
        var schedule = new ReportSchedule
        {
            Name = "Manual Approval Required",
            Frequency = ScheduleFrequency.Hourly,
            IntervalValue = 1,
            AutoExecute = false, // Air-Gapped: requires manual approval
            NextExecutionAtUtc = DateTime.UtcNow.AddMinutes(-5) // Already due
        };
        await WriteScheduleDirectlyAsync(schedule); // ← Bypass SaveScheduleAsync's auto-recalculation

        // Act
        var results = await _service.ProcessDueSchedulesAsync();

        // Assert
        Assert.Single(results);
        Assert.Equal(ExecutionOutcome.SkippedAwaitingApproval, results[0].Outcome);
        Assert.False(results[0].WasAutoExecuted);
    }

    [Fact]
    public async Task ProcessDueSchedulesAsync_AutoExecuteEnabled_ExecutesSuccessfully()
    {
        // Arrange
        var schedule = new ReportSchedule
        {
            Name = "Auto Execute",
            Frequency = ScheduleFrequency.Hourly,
            IntervalValue = 1,
            AutoExecute = true,
            NextExecutionAtUtc = DateTime.UtcNow.AddMinutes(-5) // Already due
        };
        await WriteScheduleDirectlyAsync(schedule); // ← Bypass SaveScheduleAsync's auto-recalculation

        // Act
        var results = await _service.ProcessDueSchedulesAsync();

        // Assert
        Assert.Single(results);
        Assert.Equal(ExecutionOutcome.Success, results[0].Outcome);
        Assert.True(results[0].WasAutoExecuted);
        Assert.NotNull(results[0].GeneratedFilePath);
    }

    [Fact]
    public async Task ProcessDueSchedulesAsync_NotDueYet_ReturnsEmpty()
    {
        // Arrange
        var schedule = new ReportSchedule
        {
            Name = "Future Schedule",
            Frequency = ScheduleFrequency.Daily,
            IntervalValue = 1,
            AutoExecute = true,
            NextExecutionAtUtc = DateTime.UtcNow.AddDays(1) // Not due yet
        };
        await _service.SaveScheduleAsync(schedule);

        // Act
        var results = await _service.ProcessDueSchedulesAsync();

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task ExecuteNowAsync_ManualTrigger_ExecutesSuccessfully()
    {
        // Arrange
        var schedule = CreateTestSchedule("Manual Trigger");
        await _service.SaveScheduleAsync(schedule);

        // Act
        var filePath = await _service.ExecuteNowAsync(schedule.ScheduleId, "User");

        // Assert
        Assert.NotNull(filePath);
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public async Task ExecuteNowAsync_NonExistentSchedule_ReturnsNull()
    {
        // Act
        var result = await _service.ExecuteNowAsync("non-existent-id");

        // Assert
        Assert.Null(result);
    }

    // ── Execution History Tests ──

    [Fact]
    public async Task GetExecutionHistoryAsync_AfterExecution_ReturnsLogs()
    {
        // Arrange
        var schedule = new ReportSchedule
        {
            Name = "History Test",
            Frequency = ScheduleFrequency.Hourly,
            AutoExecute = true,
            NextExecutionAtUtc = DateTime.UtcNow.AddMinutes(-5)
        };
        await WriteScheduleDirectlyAsync(schedule); // ← Bypass SaveScheduleAsync's auto-recalculation
        await _service.ProcessDueSchedulesAsync();

        // Act
        var history = await _service.GetExecutionHistoryAsync();

        // Assert
        Assert.NotEmpty(history);
        Assert.Contains(history, h => h.ScheduleName == "History Test");
    }

    // ── Helper Methods ──

    private static ReportSchedule CreateTestSchedule(string name)
    {
        return new ReportSchedule
        {
            Name = name,
            Description = "Test description",
            Frequency = ScheduleFrequency.Weekly,
            IntervalValue = 1,
            TargetDayOfWeek = DayOfWeek.Monday,
            ExecutionTime = new TimeSpan(8, 0, 0),
            AutoExecute = false, // Default to manual approval for Air-Gapped
            OutputFormat = "pdf"
        };
    }

    /// <summary>
    /// Writes a schedule directly to disk, bypassing SaveScheduleAsync's
    /// automatic NextExecutionAtUtc recalculation. Used for testing scenarios
    /// where we need a schedule that is already "due".
    /// </summary>
    private async Task WriteScheduleDirectlyAsync(ReportSchedule schedule)
    {
        var schedulesDir = Path.Combine(_tempDir, "Schedules");
        Directory.CreateDirectory(schedulesDir);

        var safeId = new string(schedule.ScheduleId.Where(char.IsLetterOrDigit).ToArray());
        var path = Path.Combine(schedulesDir, $"{safeId}.json");

        var json = System.Text.Json.JsonSerializer.Serialize(schedule, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        await File.WriteAllTextAsync(path, json);
    }

    // ── Fake Implementations ──

    private sealed class FakeStoragePathProvider : IStoragePathProvider
    {
        private readonly string _rootDir;
        private readonly string _dbPath;

        public FakeStoragePathProvider(string dbPath)
        {
            _dbPath = dbPath;
            _rootDir = Path.GetDirectoryName(dbPath) ?? Path.GetTempPath();
            Directory.CreateDirectory(_rootDir);
        }

        public string GetRootPath() => _rootDir;
        public string GetDatabasePath() => _dbPath;
        public string GetMigrationsPath() => Path.Combine(_rootDir, "Migrations");
        public string GetTempPath() => Path.Combine(_rootDir, "Temp");
        public string GetReportsPath() => Path.Combine(_rootDir, "Reports");
        public string GetBackupsPath() => Path.Combine(_rootDir, "Backups");
        public bool IsWritable() => true;
        public StoragePathInfo GetStorageInfo() => new()
        {
            RootPath = _rootDir,
            DatabasePath = _dbPath,
            RootExists = true,
            IsWritable = true,
            ConfigurationSource = "Fake"
        };
    }

    private sealed class FakeScanService : IScanService
    {
        public int TotalCheckCount => 10;

        public Task<ScanResult> RunScanAsync(ScanMode mode = ScanMode.Full, IProgress<ScanProgressUpdate>? progress = null)
        {
            var result = new ScanResult("TEST-HOST", "192.168.1.1", "00:11:22:33:44:55", "Windows 11", "22631", mode);
            result.CompleteScan();
            return Task.FromResult(result);
        }

        public Task<Finding> RescanCheckAsync(string checkId) => throw new NotImplementedException();
        public Task<Finding> RescanSubControlAsync(string checkId, string subControlId) => throw new NotImplementedException();
    }

    private sealed class FakeReportService : IReportService
    {
        public Task<string> GenerateAndSaveReportAsync(ScanResult scanResult, string outputDir, string baseFileName)
        {
            Directory.CreateDirectory(outputDir);
            var path = Path.Combine(outputDir, $"{baseFileName}.html");
            File.WriteAllText(path, "<html>Fake Report</html>");
            return Task.FromResult(path);
        }

        public Task<string> GenerateAndSaveJsonReportAsync(ScanResult scanResult, string outputDir, string baseFileName)
        {
            Directory.CreateDirectory(outputDir);
            var path = Path.Combine(outputDir, $"{baseFileName}.json");
            File.WriteAllText(path, "{}");
            return Task.FromResult(path);
        }

        public Task<string> GenerateAndSaveCsvReportAsync(ScanResult scanResult, string outputDir, string baseFileName)
        {
            Directory.CreateDirectory(outputDir);
            var path = Path.Combine(outputDir, $"{baseFileName}.csv");
            File.WriteAllText(path, "a,b,c");
            return Task.FromResult(path);
        }

        public Task<string> GeneratePdfReportAsync(ScanResult scanResult, string outputDir, string baseFileName, ReportGenerationOptions options)
        {
            Directory.CreateDirectory(outputDir);
            var path = Path.Combine(outputDir, $"{baseFileName}.pdf");
            File.WriteAllText(path, "PDF Content");
            return Task.FromResult(path);
        }

        public Task<string> GenerateExcelReportAsync(ScanResult scanResult, string outputDir, string baseFileName, ReportGenerationOptions options)
        {
            Directory.CreateDirectory(outputDir);
            var path = Path.Combine(outputDir, $"{baseFileName}.xlsx");
            File.WriteAllText(path, "Excel Content");
            return Task.FromResult(path);
        }

        public Task<string> GenerateZipBundleAsync(ScanResult scanResult, string outputDir, string baseFileName, ReportGenerationOptions options)
        {
            Directory.CreateDirectory(outputDir);
            var path = Path.Combine(outputDir, $"{baseFileName}.zip");
            File.WriteAllText(path, "ZIP Content");
            return Task.FromResult(path);
        }
    }

    private sealed class FakeReportTemplateService : IReportTemplateService
    {
        public Task<IReadOnlyList<ReportTemplate>> ListTemplatesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ReportTemplate>>(Array.Empty<ReportTemplate>());

        public Task<ReportTemplate?> GetTemplateAsync(string templateId, CancellationToken cancellationToken = default)
            => Task.FromResult<ReportTemplate?>(null);

        public Task<ReportTemplate> SaveTemplateAsync(ReportTemplate template, CancellationToken cancellationToken = default)
            => Task.FromResult(template);

        public Task<bool> DeleteTemplateAsync(string templateId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public ReportTemplate CreateFromOptions(string name, string description, ReportGenerationOptions options, string createdBy = "User")
            => new() { Name = name, Description = description };
    }

    private sealed class FakeSnapshotRepository : ISnapshotRepository
    {
        public Task SaveAsync(ScanSnapshot snapshot, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ScanSnapshot?> GetAsync(Guid snapshotId, CancellationToken cancellationToken = default) => Task.FromResult<ScanSnapshot?>(null);
        public Task<ScanSnapshot?> GetByScanIdAsync(string scanId, CancellationToken cancellationToken = default) => Task.FromResult<ScanSnapshot?>(null);
        public Task<IReadOnlyList<ScanSnapshot>> GetByAssetAsync(string hostname, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ScanSnapshot>>(Array.Empty<ScanSnapshot>());
        public Task<ScanSnapshot?> GetLatestByAssetAsync(string hostname, CancellationToken cancellationToken = default) => Task.FromResult<ScanSnapshot?>(null);
        public Task<IReadOnlyList<ScanSnapshot>> GetRecentAsync(int limit = 10, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ScanSnapshot>>(Array.Empty<ScanSnapshot>());
        public Task<IReadOnlyList<SnapshotSummary>> ListSummariesAsync(string? hostname = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SnapshotSummary>>(Array.Empty<SnapshotSummary>());
        public Task<bool> DeleteAsync(Guid snapshotId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<int> CountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> ExistsAsync(Guid snapshotId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}