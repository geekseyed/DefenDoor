namespace ISCM.Domain.ValueObjects;

public enum ScanProgressStage
{
    Initializing,
    CollectingSystemInfo,
    ExecutingChecks,
    Finalizing,
    Completed,
    Failed
}

public record ScanProgressUpdate(
    string LogMessage,
    ScanProgressStage Stage,
    int CompletedChecks,
    int TotalChecks,
    int LivePassCount,
    int LiveFailCount,
    string? CurrentCheckId = null,
    string? CurrentCheckName = null
);