namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-09: Historical Intelligence & Trend Analysis Models
/// </summary>

public class TestHistoryRecord
{
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
    public string TestId { get; set; } = string.Empty;
    public bool WasSuccessful { get; set; }
    public long DurationMs { get; set; }
    public string? ErrorMessage { get; set; }
    public string? BuildNumber { get; set; }
    public string? EnvironmentTag { get; set; } // e.g., "CI", "Local"
}

public class TrendAnalysisResult
{
    public StabilityStatus Status { get; set; } = StabilityStatus.Unknown;
    public string Message { get; set; } = string.Empty;

    public double AverageDurationMs { get; set; }
    public double DurationTrendSlope { get; set; } // ms per run
    public double FailureRateTrendSlope { get; set; } // % per window

    public int RecordCount { get; set; }
    public DateTime? FirstRunDate { get; set; }
    public DateTime? LastRunDate { get; set; }
}

public enum StabilityStatus
{
    Unknown,
    InsufficientData,
    Stable,
    Improving,
    DegradingPerformance, // Time is increasing
    DegradingStability,   // Failures are increasing
    Critical              // High failure rate + high duration
}

public class FailurePattern
{
    public string PatternId { get; set; } = Guid.NewGuid().ToString();
    public string Signature { get; set; } = string.Empty; // e.g., "Timeout after 30s"
    public List<string> RelatedTestIds { get; set; } = new();
    public int OccurrenceCount { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }

    public string? SuggestedFix { get; set; }
}