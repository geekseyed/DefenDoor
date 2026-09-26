using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-14.6: Localization Confidence Service
/// Stage 1 Evidence Support: base = FusedStrength (BF-14.1)
/// Stage 2 Signal Agreement: boost = 1 + 0.1 * (extra signals), capped 1.3
///          (signal count derived via MapToSignal - BF-14.2 single truth)
/// Stage 3 Signal Conflict: penalties High 0.40 / Medium 0.20 / Low 0.05
/// Stage 4 Calculation: score = clamp(base * boost - penalty, 0, 1)
/// Stage 5 Classification: None (no signals) / VeryLow .. VeryHigh
/// </summary>
public class LocalizationConfidenceService
{
    public const double HighConflictPenalty = 0.40;
    public const double MediumConflictPenalty = 0.20;
    public const double LowConflictPenalty = 0.05;
    public const double PerExtraSignalBoost = 0.10;
    public const double MaxAgreementBoost = 1.30;

    public LocalizationConfidenceReport Calculate(
        EvidenceFusionReport? fused,
        ConflictAnalysisReport? conflicts)
    {
        var report = new LocalizationConfidenceReport();
        if (fused is null || fused.Items.Count == 0)
            return report;

        // Stage 3 — conflict penalties per target (group keys match 14.1)
        var conflictsByTarget = (conflicts?.Conflicts ?? Enumerable.Empty<EvidenceConflict>())
            .GroupBy(c => c.TargetKey)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var item in fused.Items)
        {
            conflictsByTarget.TryGetValue(item.TargetKey, out var targetConflicts);
            targetConflicts ??= new List<EvidenceConflict>();

            // Stage 2 — agreement boost from distinct canonical signals
            var signalCount = item.Sources
                .Select(MultiSignalCorrelationService.MapToSignal)
                .Distinct()
                .Count();
            var extra = Math.Max(0, signalCount - 1);
            var boost = Math.Min(MaxAgreementBoost, 1.0 + extra * PerExtraSignalBoost);

            // Stage 3 — penalties
            var high = targetConflicts.Count(c => c.Severity == ConflictSeverity.High);
            var medium = targetConflicts.Count(c => c.Severity == ConflictSeverity.Medium);
            var low = targetConflicts.Count(c => c.Severity == ConflictSeverity.Low);
            var penalty = high * HighConflictPenalty
                          + medium * MediumConflictPenalty
                          + low * LowConflictPenalty;

            // Stage 4 — calculation
            var score = Math.Clamp(item.FusedStrength * boost - penalty, 0.0, 1.0);

            report.Targets.Add(new TargetConfidence
            {
                TargetKey = item.TargetKey,
                FusedStrength = item.FusedStrength,
                SupportingSignalCount = signalCount,
                InputCount = item.SignalCount,
                AgreementBoost = boost,
                ConflictPenalty = penalty,
                HighConflicts = high,
                MediumConflicts = medium,
                LowConflicts = low,
                ConfidenceScore = score,
                Level = Classify(score, signalCount)
            });
        }

        return Finalize(report);
    }

    // Stage 5 — classification (None only when there is no signal at all)
    private static ConfidenceLevel Classify(double score, int signalCount) =>
        signalCount == 0 ? ConfidenceLevel.None
        : score >= 0.8 ? ConfidenceLevel.VeryHigh
        : score >= 0.6 ? ConfidenceLevel.High
        : score >= 0.4 ? ConfidenceLevel.Medium
        : score >= 0.2 ? ConfidenceLevel.Low
        : ConfidenceLevel.VeryLow;

    private static LocalizationConfidenceReport Finalize(LocalizationConfidenceReport report)
    {
        report.Targets = report.Targets
            .OrderByDescending(t => t.ConfidenceScore)
            .ThenByDescending(t => t.SupportingSignalCount)
            .ThenBy(t => t.TargetKey, StringComparer.Ordinal)
            .ToList();

        report.TotalTargets = report.Targets.Count;
        report.HighConfidenceCount = report.Targets
            .Count(t => t.Level is ConfidenceLevel.High or ConfidenceLevel.VeryHigh);
        report.LowConfidenceCount = report.Targets
            .Count(t => t.Level is ConfidenceLevel.None or ConfidenceLevel.VeryLow or ConfidenceLevel.Low);
        report.TopTarget = report.Targets.FirstOrDefault();
        return report;
    }
}