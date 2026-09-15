using System.Text.Json;
using ISCM.Application.Interfaces;
using ISCM.Application.Reporting;
using ISCM.Domain.Entities;
using ISCM.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace ISCM.Application.Services;

/// <summary>
/// Manages scheduled report jobs with file-based persistence.
/// Phase 16.4: Air-Gapped compatible (no email, disk-only output).
/// </summary>
public class ScheduledReportService : IScheduledReportService
{
    private readonly IStoragePathProvider _pathProvider;
    private readonly IReportService _reportService;
    private readonly IReportTemplateService _templateService;
    private readonly IScanService _scanService;
    private readonly ISnapshotRepository _snapshotRepository;
    private readonly ILogger<ScheduledReportService> _logger;

    private readonly string _schedulesDirectory;
    private readonly string _logsDirectory;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ScheduledReportService(
        IStoragePathProvider pathProvider,
        IReportService reportService,
        IReportTemplateService templateService,
        IScanService scanService,
        ISnapshotRepository snapshotRepository,
        ILogger<ScheduledReportService> logger)
    {
        _pathProvider = pathProvider;
        _reportService = reportService;
        _templateService = templateService;
        _scanService = scanService;
        _snapshotRepository = snapshotRepository;
        _logger = logger;

        var dbPath = _pathProvider.GetDatabasePath();
        var dataDir = Path.GetDirectoryName(dbPath) ?? AppContext.BaseDirectory;
        _schedulesDirectory = Path.Combine(dataDir, "Schedules");
        _logsDirectory = Path.Combine(dataDir, "ScheduleLogs");
        Directory.CreateDirectory(_schedulesDirectory);
        Directory.CreateDirectory(_logsDirectory);
    }

    public async Task<IReadOnlyList<ReportSchedule>> ListSchedulesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<ReportSchedule>();
        await _lock.WaitAsync(cancellationToken);
        try
        {
            foreach (var file in Directory.GetFiles(_schedulesDirectory, "*.json"))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file, cancellationToken);
                    var schedule = JsonSerializer.Deserialize<ReportSchedule>(json, JsonOptions);
                    if (schedule != null) result.Add(schedule);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read schedule file: {File}", file);
                }
            }
        }
        finally { _lock.Release(); }

        return result.OrderBy(s => s.NextExecutionAtUtc ?? DateTime.MaxValue).ToList();
    }

    public async Task<ReportSchedule?> GetScheduleAsync(string scheduleId, CancellationToken cancellationToken = default)
    {
        var path = GetSchedulePath(scheduleId);
        if (!File.Exists(path)) return null;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize<ReportSchedule>(json, JsonOptions);
        }
        finally { _lock.Release(); }
    }

    public async Task<ReportSchedule> SaveScheduleAsync(ReportSchedule schedule, CancellationToken cancellationToken = default)
    {
        if (schedule == null) throw new ArgumentNullException(nameof(schedule));

        var path = GetSchedulePath(schedule.ScheduleId);

        // Calculate next execution if not set
        var savedSchedule = schedule;
        if (schedule.NextExecutionAtUtc == null || schedule.NextExecutionAtUtc < DateTime.UtcNow)
        {
            var next = schedule.CalculateNextExecution(DateTime.UtcNow);
            savedSchedule = CloneWithNextExecution(schedule, next);
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var json = JsonSerializer.Serialize(savedSchedule, JsonOptions);
            await File.WriteAllTextAsync(path, json, cancellationToken);
            _logger.LogInformation("Schedule saved: {Id} '{Name}'", savedSchedule.ScheduleId, savedSchedule.Name);
            return savedSchedule;
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> DeleteScheduleAsync(string scheduleId, CancellationToken cancellationToken = default)
    {
        var path = GetSchedulePath(scheduleId);
        if (!File.Exists(path)) return false;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            File.Delete(path);
            _logger.LogInformation("Schedule deleted: {Id}", scheduleId);
            return true;
        }
        finally { _lock.Release(); }
    }

    public async Task<string?> ExecuteNowAsync(string scheduleId, string triggeredBy = "User", CancellationToken cancellationToken = default)
    {
        var schedule = await GetScheduleAsync(scheduleId, cancellationToken);
        if (schedule == null)
        {
            _logger.LogWarning("ExecuteNow called for non-existent schedule: {Id}", scheduleId);
            return null;
        }

        var log = new ScheduleExecutionLog
        {
            ScheduleId = schedule.ScheduleId,
            ScheduleName = schedule.Name,
            TriggeredBy = triggeredBy,
            WasAutoExecuted = false
        };

        try
        {
            var filePath = await RunReportGenerationAsync(schedule);
            log.CompletedAtUtc = DateTime.UtcNow;
            log.Outcome = ExecutionOutcome.Success;
            log.GeneratedFilePath = filePath;

            await MarkExecutedInternalAsync(schedule, "Success", null, cancellationToken);
        }
        catch (Exception ex)
        {
            log.CompletedAtUtc = DateTime.UtcNow;
            log.Outcome = ExecutionOutcome.Failed;
            log.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Schedule execution failed: {Id}", scheduleId);

            await MarkExecutedInternalAsync(schedule, "Failed", ex.Message, cancellationToken);
        }
        finally
        {
            await SaveExecutionLogAsync(log, cancellationToken);
        }

        return log.GeneratedFilePath;
    }

    public async Task<IReadOnlyList<ScheduleExecutionLog>> ProcessDueSchedulesAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<ScheduleExecutionLog>();
        var schedules = await ListSchedulesAsync(cancellationToken);
        var now = DateTime.UtcNow;

        foreach (var schedule in schedules)
        {
            if (!schedule.IsActive) continue;
            if (schedule.NextExecutionAtUtc == null || schedule.NextExecutionAtUtc > now) continue;

            var log = new ScheduleExecutionLog
            {
                ScheduleId = schedule.ScheduleId,
                ScheduleName = schedule.Name,
                TriggeredBy = "System",
                WasAutoExecuted = schedule.AutoExecute
            };

            if (!schedule.AutoExecute)
            {
                // Requires user approval - just notify
                log.Outcome = ExecutionOutcome.SkippedAwaitingApproval;
                log.CompletedAtUtc = now;
                _logger.LogInformation("Schedule '{Name}' is due but requires user approval", schedule.Name);
                await SaveExecutionLogAsync(log, cancellationToken);
                results.Add(log);

                // Still advance the NextExecution so we don't keep re-notifying
                await MarkExecutedInternalAsync(schedule, "AwaitingApproval", null, cancellationToken);
                continue;
            }

            // Auto-execute
            try
            {
                var filePath = await RunReportGenerationAsync(schedule);
                log.Outcome = ExecutionOutcome.Success;
                log.GeneratedFilePath = filePath;
                log.CompletedAtUtc = DateTime.UtcNow;
                await MarkExecutedInternalAsync(schedule, "Success", null, cancellationToken);
                _logger.LogInformation("Auto-executed schedule '{Name}' -> {Path}", schedule.Name, filePath);
            }
            catch (Exception ex)
            {
                log.Outcome = ExecutionOutcome.Failed;
                log.ErrorMessage = ex.Message;
                log.CompletedAtUtc = DateTime.UtcNow;
                await MarkExecutedInternalAsync(schedule, "Failed", ex.Message, cancellationToken);
                _logger.LogError(ex, "Auto-execution failed for schedule '{Name}'", schedule.Name);
            }

            await SaveExecutionLogAsync(log, cancellationToken);
            results.Add(log);
        }

        return results;
    }

    public async Task<IReadOnlyList<ScheduleExecutionLog>> GetExecutionHistoryAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        var result = new List<ScheduleExecutionLog>();
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var files = Directory.GetFiles(_logsDirectory, "*.json")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(limit);

            foreach (var file in files)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file.FullName, cancellationToken);
                    var log = JsonSerializer.Deserialize<ScheduleExecutionLog>(json, JsonOptions);
                    if (log != null) result.Add(log);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read execution log: {File}", file.Name);
                }
            }
        }
        finally { _lock.Release(); }

        return result;
    }

    // ── Private Helpers ──

    private async Task<string> RunReportGenerationAsync(ReportSchedule schedule)
    {
        // Execute scan via IScanService (correct method name: RunScanAsync)
        // In Phase 19+, this will dispatch to an Agent via TargetHostname
        var scanResult = await _scanService.RunScanAsync(ScanMode.Full);

        var outputDir = _pathProvider.GetReportsPath();
        Directory.CreateDirectory(outputDir);
        var ts = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var safeName = string.Join("_", schedule.Name.Split(Path.GetInvalidFileNameChars()));
        var baseFileName = $"Scheduled_{safeName}_{ts}";

        // Apply template options if specified
        var options = new ReportGenerationOptions();
        if (!string.IsNullOrEmpty(schedule.TemplateId))
        {
            var template = await _templateService.GetTemplateAsync(schedule.TemplateId);
            if (template != null)
            {
                options = template.ToGenerationOptions();
            }
        }

        return schedule.OutputFormat switch
        {
            "html" => await _reportService.GenerateAndSaveReportAsync(scanResult, outputDir, baseFileName),
            "json" => await _reportService.GenerateAndSaveJsonReportAsync(scanResult, outputDir, baseFileName),
            "csv" => await _reportService.GenerateAndSaveCsvReportAsync(scanResult, outputDir, baseFileName),
            "pdf" => await _reportService.GeneratePdfReportAsync(scanResult, outputDir, baseFileName, options),
            "excel" => await _reportService.GenerateExcelReportAsync(scanResult, outputDir, baseFileName, options),
            "zip" => await _reportService.GenerateZipBundleAsync(scanResult, outputDir, baseFileName, options),
            _ => await _reportService.GenerateAndSaveReportAsync(scanResult, outputDir, baseFileName)
        };
    }

    private async Task MarkExecutedInternalAsync(ReportSchedule schedule, string status, string? error, CancellationToken cancellationToken)
    {
        // Read, update, write
        var path = GetSchedulePath(schedule.ScheduleId);
        if (!File.Exists(path)) return;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            var current = JsonSerializer.Deserialize<ReportSchedule>(json, JsonOptions);
            if (current == null) return;

            var updated = new ReportSchedule
            {
                ScheduleId = current.ScheduleId,
                Name = current.Name,
                Description = current.Description,
                CreatedAtUtc = current.CreatedAtUtc,
                IsActive = current.IsActive,
                Frequency = current.Frequency,
                IntervalValue = current.IntervalValue,
                ExecutionTime = current.ExecutionTime,
                TargetDayOfWeek = current.TargetDayOfWeek,
                DayOfMonth = current.DayOfMonth,
                TemplateId = current.TemplateId,
                TargetHostname = current.TargetHostname,
                ReportType = current.ReportType,
                OutputFormat = current.OutputFormat,
                AutoExecute = current.AutoExecute,
                SaveToFile = current.SaveToFile,
                LastExecutedAtUtc = DateTime.UtcNow,
                NextExecutionAtUtc = current.CalculateNextExecution(DateTime.UtcNow),
                ExecutionCount = current.ExecutionCount + 1,
                LastExecutionStatus = status,
                LastExecutionError = error
            };

            var newJson = JsonSerializer.Serialize(updated, JsonOptions);
            await File.WriteAllTextAsync(path, newJson, cancellationToken);
        }
        finally { _lock.Release(); }
    }

    private async Task SaveExecutionLogAsync(ScheduleExecutionLog log, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_logsDirectory, $"{log.LogId}.json");
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var json = JsonSerializer.Serialize(log, JsonOptions);
            await File.WriteAllTextAsync(path, json, cancellationToken);
        }
        finally { _lock.Release(); }
    }

    private string GetSchedulePath(string scheduleId)
    {
        var safeId = new string(scheduleId.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrEmpty(safeId)) safeId = Guid.NewGuid().ToString("N");
        return Path.Combine(_schedulesDirectory, $"{safeId}.json");
    }

    private static ReportSchedule CloneWithNextExecution(ReportSchedule s, DateTime next)
    {
        return new ReportSchedule
        {
            ScheduleId = s.ScheduleId,
            Name = s.Name,
            Description = s.Description,
            CreatedAtUtc = s.CreatedAtUtc,
            IsActive = s.IsActive,
            Frequency = s.Frequency,
            IntervalValue = s.IntervalValue,
            ExecutionTime = s.ExecutionTime,
            TargetDayOfWeek = s.TargetDayOfWeek,
            DayOfMonth = s.DayOfMonth,
            TemplateId = s.TemplateId,
            TargetHostname = s.TargetHostname,
            ReportType = s.ReportType,
            OutputFormat = s.OutputFormat,
            AutoExecute = s.AutoExecute,
            SaveToFile = s.SaveToFile,
            LastExecutedAtUtc = s.LastExecutedAtUtc,
            NextExecutionAtUtc = next,
            ExecutionCount = s.ExecutionCount,
            LastExecutionStatus = s.LastExecutionStatus,
            LastExecutionError = s.LastExecutionError
        };
    }
}