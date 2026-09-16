namespace ISCM.Application.Analytics;

/// <summary>
/// Executive-level Key Performance Indicators for compliance posture.
/// Phase 16.5: High-level metrics for management dashboards.
/// </summary>
public sealed class ExecutiveKpi
{
    // ── Current State ──
    public int CurrentScore { get; init; }
    public string CurrentGrade { get; init; } = string.Empty;
    public int TotalChecks { get; init; }
    public int PassedChecks { get; init; }
    public int FailedChecks { get; init; }
    public int CompliancePercentage => TotalChecks == 0 ? 0 : (int)Math.Round((double)PassedChecks / TotalChecks * 100);

    // ── Historical Trends ──
    public int AverageScoreLast30Days { get; init; }
    public int AverageScoreLast90Days { get; init; }
    public int TotalScansAllTime { get; init; }
    public int TotalScansLast30Days { get; init; }
    public int TotalScansLast90Days { get; init; } 
    public TrendDirection Trend30Day { get; init; } = TrendDirection.Stable;
    public TrendDirection Trend90Day { get; init; } = TrendDirection.Stable;

    // ── Performance Metrics ──
    public int DaysSinceLastScan { get; init; }
    public int DaysSinceLastImprovement { get; init; }
    public int LongestStreakWithoutRegression { get; init; }
    public int ConsecutiveImprovements { get; init; }

    // ── Risk Indicators ──
    public int CriticalFailures { get; init; }
    public int HighFailures { get; init; }
    public int MediumFailures { get; init; }
    public int LowFailures { get; init; }
    public string TopFailingControl { get; init; } = string.Empty;
    public int TopFailingControlCount { get; init; }

    // ── SLA & Compliance ──
    public bool MeetsMinimumThreshold { get; init; }
    public bool MeetsTargetThreshold { get; init; }
    public int DaysUntilNextScheduledScan { get; init; }
    public int OverdueRemediations { get; init; }
}