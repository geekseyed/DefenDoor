using ISCM.Application.Analytics;
using ISCM.Application.Interfaces;
using ISCM.Application.Snapshots;

namespace ISCM.Application.Services;

/// <summary>
/// Analyzes historical compliance trends across scan snapshots.
/// Phase 16.2 implementation.
/// </summary>
public class TrendAnalysisService : ITrendAnalysisService
{
    private readonly ISnapshotRepository _snapshotRepository;

    public TrendAnalysisService(ISnapshotRepository snapshotRepository)
    {
        _snapshotRepository = snapshotRepository ?? throw new ArgumentNullException(nameof(snapshotRepository));
    }

    public async Task<TrendAnalysisResult> AnalyzeAssetTrendAsync(
        string hostname,
        int maxDataPoints = 50,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hostname))
            throw new ArgumentException("Hostname cannot be empty.", nameof(hostname));

        var snapshots = await _snapshotRepository.GetByAssetAsync(hostname, cancellationToken);
        var ordered = snapshots
            .OrderBy(s => s.CompletedAtUtc)
            .TakeLast(maxDataPoints)
            .ToList();

        return BuildTrendResult(ordered);
    }

    public async Task<TrendAnalysisResult> AnalyzeGlobalTrendAsync(
        int maxDataPoints = 50,
        CancellationToken cancellationToken = default)
    {
        // For global view: fetch the most recent snapshot for EACH distinct hostname,
        // then order chronologically and cap at maxDataPoints.
        var recentSnapshots = await _snapshotRepository.GetRecentAsync(limit: 200, cancellationToken);

        var distinctByHost = recentSnapshots
            .GroupBy(s => s.Hostname, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First()) // already ordered by CompletedAtUtc desc
            .OrderBy(s => s.CompletedAtUtc)
            .TakeLast(maxDataPoints)
            .ToList();

        return BuildTrendResult(distinctByHost);
    }

    private static TrendAnalysisResult BuildTrendResult(IReadOnlyList<ScanSnapshot> orderedSnapshots)
    {
        if (orderedSnapshots.Count == 0)
        {
            return new TrendAnalysisResult
            {
                DataPoints = Array.Empty<TrendDataPoint>(),
                Direction = TrendDirection.InsufficientData,
                AverageScore = 0,
                MinScore = 0,
                MaxScore = 0,
                TotalScans = 0,
                OverallDelta = 0
            };
        }

        var points = new List<TrendDataPoint>(orderedSnapshots.Count);
        int? previousScore = null;

        foreach (var snap in orderedSnapshots)
        {
            var delta = previousScore.HasValue ? snap.ComplianceScore - previousScore.Value : (int?)null;
            points.Add(new TrendDataPoint
            {
                Timestamp = snap.CompletedAtUtc,
                ComplianceScore = snap.ComplianceScore,
                Grade = snap.Grade ?? string.Empty,
                PassCount = snap.PassCount,
                FailCount = snap.FailCount,
                Hostname = snap.Hostname ?? string.Empty,
                SnapshotId = snap.SnapshotId,
                ScoreDelta = delta
            });
            previousScore = snap.ComplianceScore;
        }

        var scores = points.Select(p => p.ComplianceScore).ToList();
        var overallDelta = points.Count >= 2
            ? points[^1].ComplianceScore - points[0].ComplianceScore
            : 0;

        return new TrendAnalysisResult
        {
            DataPoints = points,
            Direction = DetermineDirection(overallDelta, points.Count),
            AverageScore = scores.Count > 0 ? Math.Round(scores.Average(), 1) : 0,
            MinScore = scores.Count > 0 ? scores.Min() : 0,
            MaxScore = scores.Count > 0 ? scores.Max() : 0,
            TotalScans = points.Count,
            OverallDelta = overallDelta
        };
    }

    private static TrendDirection DetermineDirection(int overallDelta, int dataPointCount)
    {
        if (dataPointCount < 2)
            return TrendDirection.InsufficientData;
        if (overallDelta > 3)
            return TrendDirection.Improving;
        if (overallDelta < -3)
            return TrendDirection.Regressing;
        return TrendDirection.Stable;
    }
}