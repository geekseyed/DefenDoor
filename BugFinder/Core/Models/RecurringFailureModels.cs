namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-11.3: Recurring Failure Detection Models
/// </summary>

public class RecurringFailureAnalysisResult
{
    public string FailureSignature { get; set; } = string.Empty;
    public bool IsRecurring { get; set; }
    public int TotalOccurrences { get; set; }
    public double RecurrenceScore { get; set; } // 0.0 to 1.0

    public RecurrencePattern? DetectedPattern { get; set; }
    public List<FailureRecord> MatchingRecords { get; set; } = new();

    public string AnalysisSummary { get; set; } = string.Empty;
}

public enum RecurrencePattern
{
    None,           // First time occurrence
    Sporadic,       // Occasional repeats, no clear pattern
    Periodic,       // Happens at regular intervals/commits
    Persistent,     // Happens in almost every run/commit since first seen
    Regressed       // Was fixed, now back again
}

public class RecurrenceConfig
{
    public int MinimumOccurrencesForPattern { get; set; } = 3;
    public double HighConfidenceThreshold { get; set; } = 0.8;
    public int DaysToConsiderRecent { get; set; } = 30;
}