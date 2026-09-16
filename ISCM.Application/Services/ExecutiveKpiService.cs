using ISCM.Application.Analytics;
using ISCM.Application.Interfaces;
using ISCM.Application.Snapshots;
using ISCM.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace ISCM.Application.Services;

/// <summary>
/// Calculates executive-level KPIs from historical scan snapshots.
/// Phase 16.5: Provides high-level metrics for management dashboards.
/// </summary>
public class ExecutiveKpiService : IExecutiveKpiService
{
    private readonly ISnapshotRepository _snapshotRepository;
    private readonly ITrendAnalysisService _trendService;
    private readonly IScheduledReportService _scheduleService;
    private readonly ILogger<ExecutiveKpiService> _logger;

    public ExecutiveKpiService(
        ISnapshotRepository snapshotRepository,
        ITrendAnalysisService trendService,
        IScheduledReportService scheduleService,
        ILogger<ExecutiveKpiService> logger)
    {
        _snapshotRepository = snapshotRepository ?? throw new ArgumentNullException(nameof(snapshotRepository));
        _trendService = trendService ?? throw new ArgumentNullException(nameof(trendService));
        _scheduleService = scheduleService ?? throw new ArgumentNullException(nameof(scheduleService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ExecutiveKpi> CalculateKpisAsync(string? hostname = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var now = DateTime.UtcNow;
            var thirtyDaysAgo = now.AddDays(-30);
            var ninetyDaysAgo = now.AddDays(-90);

            // Get all snapshots (or filtered by hostname)
            var allSnapshots = hostname != null
                ? await _snapshotRepository.GetByAssetAsync(hostname, cancellationToken)
                : await _snapshotRepository.GetRecentAsync(limit: 1000, cancellationToken);

            if (allSnapshots.Count == 0)
            {
                return CreateEmptyKpi();
            }

            var latest = allSnapshots.First(); // Ordered by CompletedAtUtc descending
            var last30Days = allSnapshots.Where(s => s.CompletedAtUtc >= thirtyDaysAgo).ToList();
            var last90Days = allSnapshots.Where(s => s.CompletedAtUtc >= ninetyDaysAgo).ToList();

            // Calculate trends
            var trend30 = await CalculateTrendAsync(last30Days, cancellationToken);
            var trend90 = await CalculateTrendAsync(last90Days, cancellationToken);

            // Calculate streaks
            var (longestStreak, consecutiveImprovements) = CalculateStreaks(allSnapshots);

            // Calculate risk indicators from latest snapshot
            var (critical, high, medium, low) = CountFailuresBySeverity(latest);
            var (topFailingControl, topFailingCount) = GetTopFailingControl(latest);

            // Calculate days until next scheduled scan
            var daysUntilNext = await CalculateDaysUntilNextScanAsync(cancellationToken);

            return new ExecutiveKpi
            {
                // Current State
                CurrentScore = latest.ComplianceScore,
                CurrentGrade = latest.Grade,
                TotalChecks = latest.TotalControlCount,
                PassedChecks = latest.PassCount,
                FailedChecks = latest.FailCount,

                // Historical Trends
                AverageScoreLast30Days = last30Days.Any() ? (int)last30Days.Average(s => s.ComplianceScore) : 0,
                AverageScoreLast90Days = last90Days.Any() ? (int)last90Days.Average(s => s.ComplianceScore) : 0,
                TotalScansAllTime = allSnapshots.Count,
                TotalScansLast30Days = last30Days.Count,
                TotalScansLast90Days = last90Days.Count, // ← این خط را اضافه کنید
                Trend30Day = trend30,
                Trend90Day = trend90,

                // Performance Metrics
                DaysSinceLastScan = (int)(now - latest.CompletedAtUtc).TotalDays,
                DaysSinceLastImprovement = CalculateDaysSinceLastImprovement(allSnapshots, now),
                LongestStreakWithoutRegression = longestStreak,
                ConsecutiveImprovements = consecutiveImprovements,

                // Risk Indicators
                CriticalFailures = critical,
                HighFailures = high,
                MediumFailures = medium,
                LowFailures = low,
                TopFailingControl = topFailingControl,
                TopFailingControlCount = topFailingCount,

                // SLA & Compliance
                MeetsMinimumThreshold = latest.ComplianceScore >= 70,
                MeetsTargetThreshold = latest.ComplianceScore >= 85,
                DaysUntilNextScheduledScan = daysUntilNext,
                OverdueRemediations = 0 // Future: integrate with remediation tracking
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate executive KPIs");
            return CreateEmptyKpi();
        }
    }

    public async Task<ExecutiveKpi> CalculateKpisForPeriodAsync(
        DateTime fromDate,
        DateTime toDate,
        string? hostname = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var allSnapshots = hostname != null
                ? await _snapshotRepository.GetByAssetAsync(hostname, cancellationToken)
                : await _snapshotRepository.GetRecentAsync(limit: 1000, cancellationToken);

            var periodSnapshots = allSnapshots
                .Where(s => s.CompletedAtUtc >= fromDate && s.CompletedAtUtc <= toDate)
                .OrderByDescending(s => s.CompletedAtUtc)
                .ToList();

            if (periodSnapshots.Count == 0)
            {
                return CreateEmptyKpi();
            }

            var latest = periodSnapshots.First();
            var trend = await CalculateTrendAsync(periodSnapshots, cancellationToken);
            var (longestStreak, consecutiveImprovements) = CalculateStreaks(periodSnapshots);
            var (critical, high, medium, low) = CountFailuresBySeverity(latest);
            var (topFailingControl, topFailingCount) = GetTopFailingControl(latest);

            return new ExecutiveKpi
            {
                CurrentScore = latest.ComplianceScore,
                CurrentGrade = latest.Grade,
                TotalChecks = latest.TotalControlCount,
                PassedChecks = latest.PassCount,
                FailedChecks = latest.FailCount,

                AverageScoreLast30Days = (int)periodSnapshots.Average(s => s.ComplianceScore),
                AverageScoreLast90Days = (int)periodSnapshots.Average(s => s.ComplianceScore),
                TotalScansAllTime = periodSnapshots.Count,
                TotalScansLast30Days = periodSnapshots.Count,
                TotalScansLast90Days = periodSnapshots.Count, // ← این خط را اضافه کنید


                Trend30Day = trend,
                Trend90Day = trend,

                DaysSinceLastScan = (int)(DateTime.UtcNow - latest.CompletedAtUtc).TotalDays,
                DaysSinceLastImprovement = CalculateDaysSinceLastImprovement(periodSnapshots, DateTime.UtcNow),
                LongestStreakWithoutRegression = longestStreak,
                ConsecutiveImprovements = consecutiveImprovements,

                CriticalFailures = critical,
                HighFailures = high,
                MediumFailures = medium,
                LowFailures = low,
                TopFailingControl = topFailingControl,
                TopFailingControlCount = topFailingCount,

                MeetsMinimumThreshold = latest.ComplianceScore >= 70,
                MeetsTargetThreshold = latest.ComplianceScore >= 85,
                DaysUntilNextScheduledScan = 0,
                OverdueRemediations = 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate KPIs for period {From} to {To}", fromDate, toDate);
            return CreateEmptyKpi();
        }
    }

    // ── Private Helpers ──

    private static ExecutiveKpi CreateEmptyKpi() => new()
    {
        CurrentScore = 0,
        CurrentGrade = "N/A",
        TotalChecks = 0,
        PassedChecks = 0,
        FailedChecks = 0,
        AverageScoreLast30Days = 0,
        AverageScoreLast90Days = 0,
        TotalScansAllTime = 0,
        TotalScansLast30Days = 0,
        TotalScansLast90Days = 0, 
        Trend30Day = TrendDirection.InsufficientData,
        Trend90Day = TrendDirection.InsufficientData,
        DaysSinceLastScan = 0,
        DaysSinceLastImprovement = 0,
        LongestStreakWithoutRegression = 0,
        ConsecutiveImprovements = 0,
        CriticalFailures = 0,
        HighFailures = 0,
        MediumFailures = 0,
        LowFailures = 0,
        TopFailingControl = "N/A",
        TopFailingControlCount = 0,
        MeetsMinimumThreshold = false,
        MeetsTargetThreshold = false,
        DaysUntilNextScheduledScan = 0,
        OverdueRemediations = 0
    };

    private async Task<TrendDirection> CalculateTrendAsync(
        IReadOnlyList<ScanSnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        if (snapshots.Count < 2)
            return TrendDirection.InsufficientData;

        try
        {
            var trendResult = await _trendService.AnalyzeGlobalTrendAsync(maxDataPoints: snapshots.Count, cancellationToken);
            return trendResult.Direction;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate trend direction");
            return TrendDirection.Stable;
        }
    }

    private static (int longestStreak, int consecutiveImprovements) CalculateStreaks(IReadOnlyList<ScanSnapshot> snapshots)
    {
        if (snapshots.Count < 2)
            return (0, 0);

        var orderedSnapshots = snapshots.OrderBy(s => s.CompletedAtUtc).ToList();
        var longestStreak = 0;
        var currentStreak = 0;
        var consecutiveImprovements = 0;
        var currentImprovements = 0;

        for (int i = 1; i < orderedSnapshots.Count; i++)
        {
            var prev = orderedSnapshots[i - 1];
            var curr = orderedSnapshots[i];

            // Streak without regression (score stayed same or improved)
            if (curr.ComplianceScore >= prev.ComplianceScore)
            {
                currentStreak++;
                longestStreak = Math.Max(longestStreak, currentStreak);
            }
            else
            {
                currentStreak = 0;
            }

            // Consecutive improvements (strictly better)
            if (curr.ComplianceScore > prev.ComplianceScore)
            {
                currentImprovements++;
            }
            else
            {
                currentImprovements = 0;
            }
        }

        consecutiveImprovements = currentImprovements;
        return (longestStreak, consecutiveImprovements);
    }

    private static int CalculateDaysSinceLastImprovement(IReadOnlyList<ScanSnapshot> snapshots, DateTime now)
    {
        if (snapshots.Count < 2)
            return 0;

        var orderedSnapshots = snapshots.OrderBy(s => s.CompletedAtUtc).ToList();

        for (int i = orderedSnapshots.Count - 1; i > 0; i--)
        {
            var prev = orderedSnapshots[i - 1];
            var curr = orderedSnapshots[i];

            if (curr.ComplianceScore > prev.ComplianceScore)
            {
                return (int)(now - curr.CompletedAtUtc).TotalDays;
            }
        }

        return 0;
    }

    private static (int critical, int high, int medium, int low) CountFailuresBySeverity(ScanSnapshot snapshot)
    {
        var failedFindings = snapshot.Findings.Where(f => f.Status == CheckStatus.Fail).ToList();

        return (
            critical: failedFindings.Count(f => f.Severity == CheckSeverity.Critical),
            high: failedFindings.Count(f => f.Severity == CheckSeverity.High),
            medium: failedFindings.Count(f => f.Severity == CheckSeverity.Medium),
            low: failedFindings.Count(f => f.Severity == CheckSeverity.Low)
        );
    }

    private static (string controlName, int failCount) GetTopFailingControl(ScanSnapshot snapshot)
    {
        if (snapshot.Findings.Count == 0)
            return ("N/A", 0);

        var groupedByCheck = snapshot.Findings
            .Where(f => f.Status == CheckStatus.Fail)
            .GroupBy(f => f.CheckId)
            .Select(g => new { CheckId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .FirstOrDefault();

        if (groupedByCheck == null)
            return ("N/A", 0);

        var finding = snapshot.Findings.First(f => f.CheckId == groupedByCheck.CheckId);
        return (finding.Name, groupedByCheck.Count);
    }

    private async Task<int> CalculateDaysUntilNextScanAsync(CancellationToken cancellationToken)
    {
        try
        {
            var schedules = await _scheduleService.ListSchedulesAsync(cancellationToken);
            var activeSchedules = schedules.Where(s => s.IsActive && s.NextExecutionAtUtc.HasValue).ToList();

            if (activeSchedules.Count == 0)
                return 0;

            var nextExecution = activeSchedules.Min(s => s.NextExecutionAtUtc!.Value);
            // محاسبه بر اساس Date (بدون ساعت) برای جلوگیری از خطای truncation
            var daysUntil = (nextExecution.Date - DateTime.UtcNow.Date).Days;
            return Math.Max(0, daysUntil);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate days until next scheduled scan");
            return 0;
        }
    }
}
