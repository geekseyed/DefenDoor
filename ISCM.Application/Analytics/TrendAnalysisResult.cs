namespace ISCM.Application.Analytics;

/// <summary>
/// Complete result of a trend analysis operation.
/// Contains data points, summary statistics, and trend direction.
/// </summary>
public sealed class TrendAnalysisResult
{
    public IReadOnlyList<TrendDataPoint> DataPoints { get; init; } = Array.Empty<TrendDataPoint>();
    public TrendDirection Direction { get; init; }
    public double AverageScore { get; init; }
    public int MinScore { get; init; }
    public int MaxScore { get; init; }
    public int TotalScans { get; init; }

    /// <summary>
    /// Overall improvement or regression from first to last scan
    /// </summary>
    public int OverallDelta { get; init; }
}

public enum TrendDirection
{
    Improving,
    Regressing,
    Stable,
    InsufficientData
}