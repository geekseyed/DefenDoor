using ISCM.Application.Interfaces;
using ISCM.Application.Reporting;
using ISCM.Web.Services;

namespace ISCM.Web.Services;

/// <summary>
/// Background service that periodically checks for due scheduled reports.
/// Phase 16.4: Runs every 30 seconds, executes due schedules per their configuration.
/// </summary>
public class ScheduledReportBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ScheduledReportBackgroundService> _logger;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    public ScheduledReportBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<ScheduledReportBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ScheduledReportBackgroundService started. Check interval: {Interval}s", CheckInterval.TotalSeconds);

        // Initial delay to allow app startup to complete
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in scheduled report background processing");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }

        _logger.LogInformation("ScheduledReportBackgroundService stopped.");
    }

    private async Task ProcessOnceAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var scheduleService = scope.ServiceProvider.GetRequiredService<IScheduledReportService>();
        var stateService = scope.ServiceProvider.GetRequiredService<ScanStateService>();

        var results = await scheduleService.ProcessDueSchedulesAsync(stoppingToken);

        foreach (var log in results)
        {
            var message = log.Outcome switch
            {
                ExecutionOutcome.Success => $"Schedule '{log.ScheduleName}' auto-executed successfully: {Path.GetFileName(log.GeneratedFilePath ?? "")}",
                ExecutionOutcome.Failed => $"Schedule '{log.ScheduleName}' failed: {log.ErrorMessage}",
                ExecutionOutcome.SkippedAwaitingApproval => $"Schedule '{log.ScheduleName}' is due - awaiting user approval (AutoExecute disabled)",
                _ => $"Schedule '{log.ScheduleName}' processed with outcome: {log.Outcome}"
            };

            stateService.LogAction($"[Scheduler] {message}");
        }
    }
}