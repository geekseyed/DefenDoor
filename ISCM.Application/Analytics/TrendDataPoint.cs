namespace ISCM.Application.Analytics;

/// <summary>
/// Represents a single data point in a historical trend analysis.
/// Used for plotting compliance scores and metrics over time.
/// </summary>
public sealed class TrendDataPoint
{
    public DateTime Timestamp { get; init; }
    public int ComplianceScore { get; init; }
    public string Grade { get; init; } = string.Empty;
    public int PassCount { get; init; }
    public int FailCount { get; init; }
    public string Hostname { get; init; } = string.Empty;
    public Guid SnapshotId { get; init; }

    /// <summary>
    /// Delta from previous point (positive = improvement, negative = regression)
    /// </summary>
    public int? ScoreDelta { get; init; }
}