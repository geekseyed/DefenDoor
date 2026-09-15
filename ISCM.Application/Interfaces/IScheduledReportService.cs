using ISCM.Application.Reporting;

namespace ISCM.Application.Interfaces;

public interface IScheduledReportService
{
    Task<IReadOnlyList<ReportSchedule>> ListSchedulesAsync(CancellationToken cancellationToken = default);
    Task<ReportSchedule?> GetScheduleAsync(string scheduleId, CancellationToken cancellationToken = default);
    Task<ReportSchedule> SaveScheduleAsync(ReportSchedule schedule, CancellationToken cancellationToken = default);
    Task<bool> DeleteScheduleAsync(string scheduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a schedule immediately (manual trigger from UI).
    /// Returns the path of the generated report, or null on failure.
    /// </summary>
    Task<string?> ExecuteNowAsync(string scheduleId, string triggeredBy = "User", CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks all active schedules and executes any that are due.
    /// Called by BackgroundService periodically.
    /// Returns list of execution logs for what happened.
    /// </summary>
    Task<IReadOnlyList<ScheduleExecutionLog>> ProcessDueSchedulesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves recent execution logs for audit/UI display.
    /// </summary>
    Task<IReadOnlyList<ScheduleExecutionLog>> GetExecutionHistoryAsync(int limit = 50, CancellationToken cancellationToken = default);
}