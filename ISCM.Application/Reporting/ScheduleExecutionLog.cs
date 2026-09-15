namespace ISCM.Application.Reporting;

/// <summary>
/// Audit log entry for a scheduled report execution.
/// Phase 16.4: Immutable record of what happened during execution.
/// </summary>
public sealed class ScheduleExecutionLog
{
    public string LogId { get; init; } = Guid.NewGuid().ToString("N");
    public string ScheduleId { get; init; } = string.Empty;
    public string ScheduleName { get; init; } = string.Empty;
    public DateTime TriggeredAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public ExecutionOutcome Outcome { get; set; } = ExecutionOutcome.Pending;
    public string? GeneratedFilePath { get; set; }
    public string? ErrorMessage { get; set; }
    public bool WasAutoExecuted { get; set; }
    public string TriggeredBy { get; init; } = "System"; // "System" | "User"
}

public enum ExecutionOutcome
{
    Pending,
    Success,
    Failed,
    SkippedAwaitingApproval,
    Cancelled
}