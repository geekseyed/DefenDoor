using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-14.4: Evidence Consistency Service
/// Stage 1 Cross-Source: >= 2 distinct sources = Corroborated
/// Stage 2 Location: distinct non-null file paths per target (case-insensitive);
///         > 1 = Divergent (hard contradiction about WHERE)
/// Stage 3 Timeline: spread of ObservedAtUtc within tolerance = Coherent
/// Stage 4 Verdict: Divergent -> Inconsistent; corroborated + no dispersal
///         -> Consistent; single-source or dispersed -> Partial; no inputs -> Unknown
/// Grouping reuses EvidenceFusionService.ResolveGroupKey (single source of truth).
/// </summary>
public class EvidenceConsistencyService
{
    private readonly TimeSpan _maxTimelineSpread;

    public EvidenceConsistencyService(TimeSpan? maxTimelineSpread = null)
    {
        _maxTimelineSpread = maxTimelineSpread ?? TimeSpan.FromHours(24);
    }

    public EvidenceConsistencyReport Evaluate(
        IEnumerable<FusionEvidenceInput>? inputs,
        EvidenceFusionReport? fused)
    {
        var report = new EvidenceConsistencyReport();
        var raw = (inputs ?? Enumerable.Empty<FusionEvidenceInput>()).ToList();

        var groups = raw
            .GroupBy(EvidenceFusionService.ResolveGroupKey)
            .ToDictionary(g => g.Key, g => g.ToList());

        var fusedByTarget = (fused?.Items ?? Enumerable.Empty<FusedEvidenceItem>())
            .GroupBy(i => i.TargetKey)
            .ToDictionary(g => g.Key, g => g.First());

        var keys = groups.Keys
            .Union(fusedByTarget.Keys)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        foreach (var key in keys)
        {
            groups.TryGetValue(key, out var targetInputs);
            targetInputs ??= new List<FusionEvidenceInput>();
            fusedByTarget.TryGetValue(key, out var fusedItem);

            var record = new TargetConsistencyRecord
            {
                TargetKey = key,
                FusedStrength = fusedItem?.FusedStrength ?? 0.0,
                InputCount = targetInputs.Count,
                DistinctSourceCount = fusedItem?.DistinctSourceCount
                    ?? targetInputs.Select(i => i.SourceType).Distinct().Count()
            };

            // Stage 1
            record.CrossSource = record.InputCount == 0
                ? CrossSourceStatus.Unknown
                : record.DistinctSourceCount >= 2
                    ? CrossSourceStatus.Corroborated
                    : CrossSourceStatus.SingleSource;

            // Stage 2
            record.Location = EvaluateLocation(targetInputs, out var distinctLocations);
            record.DistinctLocations = distinctLocations;

            // Stage 3
            record.Timeline = EvaluateTimeline(targetInputs, out var spread);
            record.TimelineSpread = spread;

            // Stage 4
            record.Verdict = Combine(record);

            report.Targets.Add(record);
        }

        return Finalize(report);
    }

    // ---------- internals ----------

    private static LocationStatus EvaluateLocation(
        List<FusionEvidenceInput> inputs, out List<string> distinct)
    {
        distinct = inputs
            .Select(i => i.TargetFilePath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        return distinct.Count switch
        {
            0 => LocationStatus.Unknown,
            1 => LocationStatus.Aligned,
            _ => LocationStatus.Divergent
        };
    }

    private TimelineStatus EvaluateTimeline(
        List<FusionEvidenceInput> inputs, out TimeSpan? spread)
    {
        var times = inputs
            .Where(i => i.ObservedAtUtc.HasValue)
            .Select(i => i.ObservedAtUtc!.Value)
            .OrderBy(t => t)
            .ToList();

        if (times.Count == 0)
        {
            spread = null;
            return TimelineStatus.Unknown;
        }

        spread = times[^1] - times[0];
        return spread.Value <= _maxTimelineSpread
            ? TimelineStatus.Coherent
            : TimelineStatus.Dispersed;
    }

    private static TargetConsistencyVerdict Combine(TargetConsistencyRecord record)
    {
        if (record.InputCount == 0)
            return TargetConsistencyVerdict.Unknown;          // fused-only target

        if (record.Location == LocationStatus.Divergent)
            return TargetConsistencyVerdict.Inconsistent;     // hard contradiction about WHERE

        if (record.CrossSource == CrossSourceStatus.Corroborated
            && record.Timeline != TimelineStatus.Dispersed)
            return TargetConsistencyVerdict.Consistent;

        return TargetConsistencyVerdict.Partial;              // single source OR dispersed
    }

    private static EvidenceConsistencyReport Finalize(EvidenceConsistencyReport report)
    {
        report.TotalTargets = report.Targets.Count;
        report.ConsistentCount = report.Targets.Count(t => t.Verdict == TargetConsistencyVerdict.Consistent);
        report.PartialCount = report.Targets.Count(t => t.Verdict == TargetConsistencyVerdict.Partial);
        report.InconsistentCount = report.Targets.Count(t => t.Verdict == TargetConsistencyVerdict.Inconsistent);
        report.UnknownCount = report.Targets.Count(t => t.Verdict == TargetConsistencyVerdict.Unknown);

        report.OverallStatus =
            report.TotalTargets == 0 ? ReportConsistencyStatus.None
            : report.InconsistentCount > 0 ? ReportConsistencyStatus.Inconsistent
            : report.ConsistentCount == report.TotalTargets ? ReportConsistencyStatus.Consistent
            : ReportConsistencyStatus.Partial;

        return report;
    }
}