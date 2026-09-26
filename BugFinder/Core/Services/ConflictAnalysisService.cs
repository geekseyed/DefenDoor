using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-14.5: Conflicting Evidence Service
/// Stage 1 Detection: from BF-14.4 records (Divergent / Dispersed flags)
/// Stage 2 Classification: Location / Timeline / SourceCount + severity
/// Stage 3 Source Comparison: which sources are on which side (from raw inputs)
/// Stage 4 Preservation: unresolved conflicts stay Unresolved (never hidden);
/// report exposes counts that feed BF-14.6 confidence calculation.
/// Grouping reuses EvidenceFusionService.ResolveGroupKey (single truth).
/// </summary>
public class ConflictAnalysisService
{
    private readonly TimeSpan _maxTimelineSpread;

    public ConflictAnalysisService(TimeSpan? maxTimelineSpread = null)
    {
        _maxTimelineSpread = maxTimelineSpread ?? TimeSpan.FromHours(24);
    }

    public ConflictAnalysisReport Analyze(
        IEnumerable<FusionEvidenceInput>? inputs,
        EvidenceConsistencyReport? consistency)
    {
        var report = new ConflictAnalysisReport();
        var raw = (inputs ?? Enumerable.Empty<FusionEvidenceInput>()).ToList();

        var groups = raw
            .GroupBy(EvidenceFusionService.ResolveGroupKey)
            .ToDictionary(g => g.Key, g => g.ToList());

        var consistencyByTarget = (consistency?.Targets ?? Enumerable.Empty<TargetConsistencyRecord>())
            .ToDictionary(t => t.TargetKey, t => t);

        // union of keys: consistency knows fused-only targets too, but only
        // raw-input groups can carry parties for Stage 3
        var keys = groups.Keys
            .Union(consistencyByTarget.Keys)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        foreach (var key in keys)
        {
            groups.TryGetValue(key, out var targetInputs);
            targetInputs ??= new List<FusionEvidenceInput>();

            consistencyByTarget.TryGetValue(key, out var record);
            var conflicts = new List<EvidenceConflict>();

            if (record is not null)
            {
                // Stage 1+2 — Location conflict (High: contradicts localization)
                if (record.Location == LocationStatus.Divergent)
                {
                    conflicts.Add(BuildLocationConflict(key, targetInputs));
                }

                // Stage 1+2 — Timeline conflict (Low/Medium by spread magnitude)
                if (record.Timeline == TimelineStatus.Dispersed && record.TimelineSpread.HasValue)
                {
                    conflicts.Add(BuildTimelineConflict(key, targetInputs, record.TimelineSpread.Value));
                }

                // Stage 1+2 — SourceCount mismatch between fused package and raw inputs (Medium)
                if (targetInputs.Count > 0 && record.DistinctSourceCount != targetInputs
                        .Select(i => i.SourceType).Distinct().Count())
                {
                    conflicts.Add(BuildSourceCountConflict(key, targetInputs, record.DistinctSourceCount));
                }
            }

            if (conflicts.Count == 0) continue;

            report.Conflicts.AddRange(conflicts);
        }

        return Finalize(report);
    }

    // ---------- internals ----------

    // Stage 2+3 — Location: High severity; parties = sources per path
    private static EvidenceConflict BuildLocationConflict(
        string key, List<FusionEvidenceInput> inputs)
    {
        var byPath = inputs
            .Where(i => !string.IsNullOrWhiteSpace(i.TargetFilePath))
            .GroupBy(i => i.TargetFilePath!.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        var details = byPath
            .Select(g => $"{g.Key} <- [{string.Join(", ", g.Select(i => i.SourceType).Distinct())}]")
            .ToList();

        return new EvidenceConflict
        {
            Kind = ConflictKind.Location,
            Severity = ConflictSeverity.High,
            TargetKey = key,
            SourcesFor = inputs.Select(i => i.SourceType).Distinct().OrderBy(s => s).ToList(),
            Details = details
        };
    }

    // Stage 2+3 — Timeline: Low if within 2x tolerance, else Medium
    private EvidenceConflict BuildTimelineConflict(
        string key, List<FusionEvidenceInput> inputs, TimeSpan spread)
    {
        var times = inputs.Where(i => i.ObservedAtUtc.HasValue)
            .Select(i => i.ObservedAtUtc!.Value).OrderBy(t => t).ToList();

        var details = times.Count >= 2
            ? new List<string> { $"observed {times.First():O} .. {times.Last():O} (spread {spread})" }
            : new List<string>();

        return new EvidenceConflict
        {
            Kind = ConflictKind.Timeline,
            Severity = spread <= _maxTimelineSpread * 2 ? ConflictSeverity.Low : ConflictSeverity.Medium,
            TargetKey = key,
            SourcesFor = inputs.Select(i => i.SourceType).Distinct().OrderBy(s => s).ToList(),
            Details = details
        };
    }

    // Stage 2+3 — SourceCount: Medium; mismatch between package and raw
    private static EvidenceConflict BuildSourceCountConflict(
        string key, List<FusionEvidenceInput> inputs, int packageSourceCount)
    {
        var rawSourceCount = inputs.Select(i => i.SourceType).Distinct().Count();
        return new EvidenceConflict
        {
            Kind = ConflictKind.SourceCount,
            Severity = ConflictSeverity.Medium,
            TargetKey = key,
            SourcesFor = inputs.Select(i => i.SourceType).Distinct().OrderBy(s => s).ToList(),
            Details = new List<string> { $"package claims {packageSourceCount} source(s), raw inputs show {rawSourceCount}" }
        };
    }

    // Stage 4 — preservation + counts
    private static ConflictAnalysisReport Finalize(ConflictAnalysisReport report)
    {
        report.TotalTargets = report.Conflicts.Select(c => c.TargetKey).Distinct().Count();
        report.ConflictedTargets = report.TotalTargets;
        report.HighSeverityCount = report.Conflicts.Count(c => c.Severity == ConflictSeverity.High);
        report.MediumSeverityCount = report.Conflicts.Count(c => c.Severity == ConflictSeverity.Medium);
        report.LowSeverityCount = report.Conflicts.Count(c => c.Severity == ConflictSeverity.Low);
        report.UnresolvedCount = report.Conflicts.Count(c => c.Resolution == ConflictResolution.Unresolved);
        return report;
    }
}