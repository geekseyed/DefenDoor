using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-14.8: Investigation Report Service
/// Assemble(): pure assembly from pre-computed BF-14.1..14.7 reports.
/// Investigate(): full-pipeline convenience - raw inputs in, wires
/// 14.1 (Fuse) -> 14.2 (Correlate) -> 14.3 (Chain) -> 14.4 (Consistency)
/// -> 14.5 (Conflicts) -> 14.6 (Confidence) -> 14.7 (Uncertainty),
/// then assembles the official report. No new computation here.
/// This is the terminal output of the Bug Finder Core (CORE STOPS HERE).
/// </summary>
public class InvestigationReportService
{
    private readonly EvidenceFusionService _fusion = new();
    private readonly MultiSignalCorrelationService _correlation = new();
    private readonly InvestigationChainService _chain = new();
    private readonly EvidenceConsistencyService _consistency = new();
    private readonly ConflictAnalysisService _conflicts = new();
    private readonly LocalizationConfidenceService _confidence = new();
    private readonly UncertaintyModelService _uncertainty = new();

    // Full pipeline — one call, official report out
    public InvestigationReport Investigate(
    FailureIdentityInput? failure,
    IReadOnlyList<FailureLocation>? locations,
    IEnumerable<FusionEvidenceInput>? rawEvidence,
    int maxSuspiciousLocations = 5)
    {
        if (maxSuspiciousLocations < 1)
            throw new ArgumentOutOfRangeException(nameof(maxSuspiciousLocations));

        // materialize once (no double enumeration downstream)
        var evidenceList = rawEvidence?.ToList();

        var hasFailure = failure is not null
                         && !string.IsNullOrWhiteSpace(failure.TestIdentity);
        var hasEvidence = evidenceList is not null && evidenceList.Count > 0;

        // No investigation ran -> minimal terminal report:
        // no Evidence summary, no Timeline (consistent with Assemble contract)
        if (!hasFailure && !hasEvidence)
        {
            return Assemble(new InvestigationReportInput
            {
                MaxSuspiciousLocations = maxSuspiciousLocations
            });
        }

        var fused = _fusion.Fuse(evidenceList);
        var correlation = _correlation.Correlate(fused);
        var chain = _chain.Build(failure, locations, fused, correlation, maxSuspiciousLocations);
        var consistency = _consistency.Evaluate(evidenceList, fused);
        var conflicts = _conflicts.Analyze(evidenceList, consistency);
        var confidence = _confidence.Calculate(fused, conflicts);
        var uncertainty = _uncertainty.Assess(fused, consistency, conflicts, confidence);

        return Assemble(new InvestigationReportInput
        {
            Failure = failure,
            Locations = locations,
            FusedEvidence = fused,
            Correlation = correlation,
            Chain = chain,
            Consistency = consistency,
            Conflicts = conflicts,
            Confidence = confidence,
            Uncertainty = uncertainty,
            MaxSuspiciousLocations = maxSuspiciousLocations
        });
    }

    // Assembly from pre-computed reports (orchestration left to caller)
    public InvestigationReport Assemble(InvestigationReportInput input)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));

        var report = new InvestigationReport
        {
            Failure = input.Failure,
            Locations = input.Locations?.ToList() ?? new List<FailureLocation>()
        };

        // Stage 4 — compact evidence summary
        if (input.FusedEvidence is not null && input.FusedEvidence.TotalInputs > 0)
        {
            report.Evidence = new EvidenceSummary
            {
                TotalInputs = input.FusedEvidence.TotalInputs,
                TotalTargets = input.FusedEvidence.TotalTargets,
                FullyCorroboratedCount = input.FusedEvidence.FullyCorroboratedCount,
                ConsistencyStatus = input.Consistency?.OverallStatus,
                ConflictCount = input.Conflicts?.Conflicts.Count ?? 0,
                UnresolvedConflictCount = input.Conflicts?.UnresolvedCount ?? 0
            };
        }

        // Stage 5 — suspicious locations: chain (already top-N) or correlation fallback
        report.SuspiciousLocations = input.Chain?.SuspiciousLocations
            ?? input.Correlation?.CorrelatedTargets
                .Take(input.MaxSuspiciousLocations).ToList()
            ?? new List<CorrelatedTarget>();

        // Stage 6 + 7 — full sub-reports
        report.Confidence = input.Confidence;
        report.Uncertainty = input.Uncertainty;

        // Stage 8 — timeline from the chain (empty when chain absent)
        report.Timeline = input.Chain?.Steps.ToList() ?? new List<InvestigationStep>();

        // Outcome
        var hasFailure = input.Failure is not null
                         && !string.IsNullOrWhiteSpace(input.Failure.TestIdentity);
        var hasEvidence = input.FusedEvidence is not null && input.FusedEvidence.TotalInputs > 0;

        report.Outcome = !(hasFailure || hasEvidence)
            ? InvestigationOutcome.InsufficientEvidence
            : input.Chain?.IsComplete == true
                ? InvestigationOutcome.Complete
                : InvestigationOutcome.Partial;

        // Backing sub-reports (self-contained terminal output)
        report.FusedEvidence = input.FusedEvidence;
        report.Correlation = input.Correlation;
        report.Chain = input.Chain;
        report.Consistency = input.Consistency;
        report.Conflicts = input.Conflicts;

        return report;
    }
}