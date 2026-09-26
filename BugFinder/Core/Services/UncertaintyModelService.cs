using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-14.7: Uncertainty Model Service
/// Stage 1 Known: input/source/signal counts (14.1) + confidence (14.6)
/// Stage 2 Missing: corroboration (sources < 2), location/timeline gaps (14.4 records)
/// Stage 3 Conflicting: conflict counts by severity (14.5)
/// Stage 4 Unknown: package-only targets / consistency not evaluated
/// Stage 5 Score: deterministic penalties, clamped [0,1]; Level thresholds
///          Low < 0.3 <= Medium < 0.6 <= High
/// Weights: Corroboration 0.30 | Location 0.20 | Timeline 0.10 | SingleSignal 0.10
///          | Conflict High 0.20 / Medium 0.10 / Low 0.05 | UnknownAspect 0.20
/// </summary>
public class UncertaintyModelService
{
    public const double MissingCorroborationPenalty = 0.30;
    public const double MissingLocationPenalty = 0.20;
    public const double MissingTimelinePenalty = 0.10;
    public const double SingleSignalPenalty = 0.10;
    public const double HighConflictPenalty = 0.20;
    public const double MediumConflictPenalty = 0.10;
    public const double LowConflictPenalty = 0.05;
    public const double UnknownAspectPenalty = 0.20;

    public InvestigationUncertaintyReport Assess(
        EvidenceFusionReport? fused,
        EvidenceConsistencyReport? consistency,
        ConflictAnalysisReport? conflicts,
        LocalizationConfidenceReport? confidence)
    {
        var report = new InvestigationUncertaintyReport();
        if (fused is null || fused.Items.Count == 0)
        {
            report.OverallLevel = UncertaintyLevel.None;
            return report;
        }

        var consistencyByTarget = (consistency?.Targets ?? Enumerable.Empty<TargetConsistencyRecord>())
            .ToDictionary(t => t.TargetKey, t => t);
        var conflictsByTarget = (conflicts?.Conflicts ?? Enumerable.Empty<EvidenceConflict>())
            .GroupBy(c => c.TargetKey)
            .ToDictionary(g => g.Key, g => g.ToList());
        var confidenceByTarget = (confidence?.Targets ?? Enumerable.Empty<TargetConfidence>())
            .ToDictionary(t => t.TargetKey, t => t);

        foreach (var item in fused.Items)
        {
            consistencyByTarget.TryGetValue(item.TargetKey, out var record);
            conflictsByTarget.TryGetValue(item.TargetKey, out var targetConflicts);
            targetConflicts ??= new List<EvidenceConflict>();
            confidenceByTarget.TryGetValue(item.TargetKey, out var knownConfidence);

            // Stage 1 — Known
            var signals = item.Sources
                .Select(MultiSignalCorrelationService.MapToSignal)
                .Distinct()
                .Count();

            var uncertainty = new TargetUncertainty
            {
                TargetKey = item.TargetKey,
                KnownInputCount = item.SignalCount,
                KnownSourceCount = item.DistinctSourceCount,
                KnownSignalCount = signals,
                KnownConfidence = knownConfidence?.ConfidenceScore ?? 0.0
            };

            // Stage 2 — Missing (explicit descriptors)
            if (item.DistinctSourceCount < 2)
            {
                uncertainty.MissingCorroboration = true;
                uncertainty.MissingEvidence.Add(
                    $"Corroboration: only {item.DistinctSourceCount} distinct source(s)");
            }
            if (record is not null && record.Location == LocationStatus.Unknown)
            {
                uncertainty.MissingLocation = true;
                uncertainty.MissingEvidence.Add("Location: no file path evidence recorded");
            }
            if (record is not null && record.Timeline == TimelineStatus.Unknown)
            {
                uncertainty.MissingTimeline = true;
                uncertainty.MissingEvidence.Add("Timeline: no observation timestamps recorded");
            }

            // Stage 3 — Conflicting
            uncertainty.ConflictCount = targetConflicts.Count;
            uncertainty.HighSeverityConflicts =
                targetConflicts.Count(c => c.Severity == ConflictSeverity.High);

            // Stage 4 — Unknown state
            if (record is not null && record.InputCount == 0)
                uncertainty.UnknownAspects.Add("no raw evidence inputs (package-only target)");
            if (record is null)
                uncertainty.UnknownAspects.Add("consistency not evaluated for this target");

            // Stage 5 — Score + Level
            var score = 0.0;
            if (uncertainty.MissingCorroboration) score += MissingCorroborationPenalty;
            if (uncertainty.MissingLocation) score += MissingLocationPenalty;
            if (uncertainty.MissingTimeline) score += MissingTimelinePenalty;
            if (signals < 2) score += SingleSignalPenalty;

            score += uncertainty.HighSeverityConflicts * HighConflictPenalty
                     + targetConflicts.Count(c => c.Severity == ConflictSeverity.Medium) * MediumConflictPenalty
                     + targetConflicts.Count(c => c.Severity == ConflictSeverity.Low) * LowConflictPenalty;

            score += uncertainty.UnknownAspects.Count * UnknownAspectPenalty;

            uncertainty.UncertaintyScore = Math.Clamp(score, 0.0, 1.0);
            uncertainty.Level = uncertainty.UncertaintyScore >= 0.6 ? UncertaintyLevel.High
                : uncertainty.UncertaintyScore >= 0.3 ? UncertaintyLevel.Medium
                : UncertaintyLevel.Low;

            report.Targets.Add(uncertainty);
        }

        return Finalize(report);
    }

    private static InvestigationUncertaintyReport Finalize(InvestigationUncertaintyReport report)
    {
        report.Targets = report.Targets
            .OrderByDescending(t => t.UncertaintyScore)
            .ThenBy(t => t.TargetKey, StringComparer.Ordinal)
            .ToList();

        report.TotalTargets = report.Targets.Count;
        report.HighUncertaintyCount = report.Targets.Count(t => t.Level == UncertaintyLevel.High);
        report.AverageUncertainty = report.TotalTargets == 0
            ? 0.0
            : report.Targets.Average(t => t.UncertaintyScore);

        report.OverallLevel = report.AverageUncertainty >= 0.6 ? UncertaintyLevel.High
            : report.AverageUncertainty >= 0.3 ? UncertaintyLevel.Medium
            : UncertaintyLevel.Low;

        return report;
    }
}