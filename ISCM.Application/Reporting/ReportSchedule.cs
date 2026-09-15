namespace ISCM.Application.Reporting;

/// <summary>
/// Represents a scheduled report job configuration.
/// Phase 16.4: Scheduled Reports (Air-Gapped, no email).
/// </summary>
public sealed class ReportSchedule
{
    public string ScheduleId { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    // ── Schedule Configuration ──
    public ScheduleFrequency Frequency { get; init; } = ScheduleFrequency.Weekly;
    public int IntervalValue { get; init; } = 1;
    public TimeSpan ExecutionTime { get; init; } = new TimeSpan(8, 0, 0);
    public DayOfWeek? TargetDayOfWeek { get; init; } = DayOfWeek.Monday;
    public int? DayOfMonth { get; init; }

    // ── Report Configuration ──
    public string TemplateId { get; init; } = string.Empty;
    public string TargetHostname { get; init; } = string.Empty;
    public string ReportType { get; init; } = "current";
    public string OutputFormat { get; init; } = "pdf";

    // ── Execution Mode (Air-Gapped friendly) ──
    /// <summary>
    /// If true, schedule executes automatically when due.
    /// If false, system only notifies via EventLog and waits for manual approval.
    /// </summary>
    public bool AutoExecute { get; init; } = false;

    /// <summary>
    /// Always true in Air-Gapped environment (reports saved to disk).
    /// </summary>
    public bool SaveToFile { get; init; } = true;

    // ── Execution Tracking ──
    public DateTime? LastExecutedAtUtc { get; set; }
    public DateTime? NextExecutionAtUtc { get; set; }
    public int ExecutionCount { get; set; } = 0;
    public string? LastExecutionStatus { get; set; }
    public string? LastExecutionError { get; set; }

    public DateTime CalculateNextExecution(DateTime fromTime)
    {
        return Frequency switch
        {
            ScheduleFrequency.Hourly => fromTime.AddHours(IntervalValue),
            ScheduleFrequency.Daily => fromTime.AddDays(IntervalValue),
            ScheduleFrequency.Weekly => CalculateNextWeekly(fromTime),
            ScheduleFrequency.Monthly => CalculateNextMonthly(fromTime),
            _ => fromTime.AddDays(7)
        };
    }

    private DateTime CalculateNextWeekly(DateTime fromTime)
    {
        var targetDay = TargetDayOfWeek ?? DayOfWeek.Monday;
        var daysUntilTarget = ((int)targetDay - (int)fromTime.DayOfWeek + 7) % 7;
        if (daysUntilTarget == 0 && fromTime.TimeOfDay > ExecutionTime)
            daysUntilTarget = 7;

        var nextDate = fromTime.AddDays(daysUntilTarget);
        return new DateTime(nextDate.Year, nextDate.Month, nextDate.Day,
            ExecutionTime.Hours, ExecutionTime.Minutes, ExecutionTime.Seconds);
    }

    private DateTime CalculateNextMonthly(DateTime fromTime)
    {
        var targetDay = DayOfMonth ?? 1;
        var nextMonth = fromTime.Month;
        var nextYear = fromTime.Year;

        if (fromTime.Day > targetDay || (fromTime.Day == targetDay && fromTime.TimeOfDay > ExecutionTime))
        {
            nextMonth++;
            if (nextMonth > 12) { nextMonth = 1; nextYear++; }
        }

        var daysInMonth = DateTime.DaysInMonth(nextYear, nextMonth);
        var actualDay = Math.Min(targetDay, daysInMonth);
        return new DateTime(nextYear, nextMonth, actualDay,
            ExecutionTime.Hours, ExecutionTime.Minutes, ExecutionTime.Seconds);
    }
}

public enum ScheduleFrequency
{
    Hourly,
    Daily,
    Weekly,
    Monthly
}