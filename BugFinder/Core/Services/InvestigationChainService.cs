using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-14.3: Investigation Chain Service
/// Stage 1 Failure + Stage 2 Classification (from FailureIdentityInput)
/// Stage 3 Localization (FailureLocations, primary required for Complete)
/// Stage 4 Evidence (BF-14.1 fused package: corroborated = Complete)
/// Stage 5 Correlation (BF-14.2: multi-signal targets = Complete)
/// Stage 6 Suspicious Locations (top-N from correlated targets)
/// Stage 7 Chain assembly (Complete only if all links Complete)
/// Principle: suspicious locations are evidence support, never root cause.
/// </summary>
public class InvestigationChainService
{
    private const InvestigationStageStatus Complete = InvestigationStageStatus.Complete;
    private const InvestigationStageStatus Partial = InvestigationStageStatus.Partial;
    private const InvestigationStageStatus Empty = InvestigationStageStatus.Empty;

    /// <summary>
    /// Assembles the investigation chain. All inputs optional — missing
    /// inputs degrade their stage (Empty/Partial), never throw.
    /// </summary>
    public InvestigationChainReport Build(
        FailureIdentityInput? failure,
        IReadOnlyList<FailureLocation>? locations,
        EvidenceFusionReport? fused,
        MultiSignalCorrelationReport? correlation,
        int maxSuspiciousLocations = 5)
    {
        if (maxSuspiciousLocations < 1)
            throw new ArgumentOutOfRangeException(nameof(maxSuspiciousLocations));

        var report = new InvestigationChainReport { Failure = failure };

        // Stage 1 — Failure
        var hasFailure = failure is not null
                         && !string.IsNullOrWhiteSpace(failure.TestIdentity);
        var failureStep = new InvestigationStep
        {
            Kind = InvestigationStageKind.Failure,
            Status = hasFailure ? Complete : Empty,
            Summary = hasFailure ? failure!.TestIdentity : "no failure identity provided"
        };

        // Stage 2 — Classification
        var classificationStep = new InvestigationStep
        {
            Kind = InvestigationStageKind.Classification,
            Summary = failure?.FailureCategory ?? "unclassified"
        };
        classificationStep.Status = failure is null
            ? Empty
            : !string.IsNullOrWhiteSpace(failure.FailureCategory) ? Complete : Partial;

        // Stage 3 — Localization
        var locs = locations ?? new List<FailureLocation>();
        var hasPrimary = locs.Any(l => l.IsPrimary);
        var localizationStep = new InvestigationStep
        {
            Kind = InvestigationStageKind.Localization,
            Status = locs.Count == 0 ? Empty : hasPrimary ? Complete : Partial,
            Summary = $"{locs.Count} location(s){(hasPrimary ? ", primary identified" : "")}"
        };

        // Stage 4 — Evidence (BF-14.1)
        var evidenceStep = new InvestigationStep { Kind = InvestigationStageKind.Evidence };
        if (fused is null || fused.TotalInputs == 0)
        {
            evidenceStep.Status = Empty;
            evidenceStep.Summary = "no fused evidence";
        }
        else if (fused.FullyCorroboratedCount == 0)
        {
            evidenceStep.Status = Partial;
            evidenceStep.Summary = $"{fused.TotalInputs} inputs -> {fused.TotalTargets} targets (0 corroborated)";
        }
        else
        {
            evidenceStep.Status = Complete;
            evidenceStep.Summary =
                $"{fused.TotalInputs} inputs -> {fused.TotalTargets} targets ({fused.FullyCorroboratedCount} corroborated)";
        }

        // Stage 5 — Correlation (BF-14.2)
        var correlationStep = new InvestigationStep { Kind = InvestigationStageKind.Correlation };
        if (correlation is null || correlation.TotalTargets == 0)
        {
            correlationStep.Status = Empty;
            correlationStep.Summary = "no signal correlation";
        }
        else if (correlation.MultiSignalTargetCount > 0)
        {
            correlationStep.Status = Complete;
            correlationStep.Summary =
                $"{correlation.Agreements.Count} agreements, {correlation.MultiSignalTargetCount} multi-signal target(s)";
        }
        else
        {
            correlationStep.Status = Partial;
            correlationStep.Summary =
                $"{correlation.Agreements.Count} agreements, single-signal targets only";
        }

        // Stage 6 — Suspicious Locations (top-N, already sorted by 14.2)
        report.SuspiciousLocations = correlation?.CorrelatedTargets
                      .Take(maxSuspiciousLocations)
                      .ToList() ?? new List<CorrelatedTarget>();

        var suspiciousStep = new InvestigationStep
        {
            Kind = InvestigationStageKind.SuspiciousLocations,
            Status = report.SuspiciousLocations.Count == 0 ? Empty : Complete,
            Summary = $"{report.SuspiciousLocations.Count} suspicious location(s)"
        };

        // Stage 7 — Chain assembly
        var links = new[] { failureStep, classificationStep, localizationStep, evidenceStep, correlationStep, suspiciousStep };
        var isComplete = links.All(s => s.Status == Complete);
        var chainStep = new InvestigationStep
        {
            Kind = InvestigationStageKind.Chain,
            Status = isComplete ? Complete : Partial,
            Summary = isComplete
                ? "complete evidence-based investigation chain"
                : "partial chain - gaps present"
        };

        report.Steps = links.Append(chainStep).ToList();
        report.IsComplete = isComplete;
        return report;
    }
}