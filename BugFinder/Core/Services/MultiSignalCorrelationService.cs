using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-14.2: Multi-Signal Correlation Service
/// Stages 1-6: build one profile per canonical signal from the fused
/// evidence package (BF-14.1 output = input contract).
/// Stage 7: pairwise signal agreements + per-target supporting signals.
/// Principle: correlation is observational only — agreement between
/// signals is evidence support, never proof of root cause.
/// </summary>
public class MultiSignalCorrelationService
{
    // Stage 1-6 mapping: evidence source vocabulary -> canonical signal
    public static InvestigationSignal MapToSignal(CandidateEvidenceType source) => source switch
    {
        CandidateEvidenceType.TestFailure
            or CandidateEvidenceType.DomainFailure
            or CandidateEvidenceType.Sbfl
            or CandidateEvidenceType.MultiTest => InvestigationSignal.Test,
        CandidateEvidenceType.Stack => InvestigationSignal.Stack,
        CandidateEvidenceType.Coverage => InvestigationSignal.Coverage,
        CandidateEvidenceType.Runtime => InvestigationSignal.Runtime,
        CandidateEvidenceType.Regression => InvestigationSignal.Regression,
        CandidateEvidenceType.Historical => InvestigationSignal.Historical,
        CandidateEvidenceType.StaticCorrelation => InvestigationSignal.Static,
        _ => InvestigationSignal.Test
    };

    // Stage 7 — full correlation over the fused package
    public MultiSignalCorrelationReport Correlate(EvidenceFusionReport? fused)
    {
        var report = new MultiSignalCorrelationReport();
        if (fused is null)
            return report;

        // Stages 1-6: per-signal profiles
        foreach (var signal in Enum.GetValues<InvestigationSignal>())
        {
            var targets = fused.Items
                .Where(item => item.Sources.Any(s => MapToSignal(s) == signal))
                .Select(item => new SignalTargetSupport
                {
                    TargetKey = item.TargetKey,
                    FusedStrength = item.FusedStrength
                })
                .OrderByDescending(t => t.FusedStrength)
                .ToList();

            report.Profiles.Add(new SignalProfile
            {
                Signal = signal,
                Targets = targets,
                TargetCount = targets.Count
            });
        }

        // Stage 7a: pairwise agreements (active signals only)
        var active = report.Profiles.Where(p => p.TargetCount > 0).ToList();
        for (var i = 0; i < active.Count; i++)
        {
            for (var j = i + 1; j < active.Count; j++)
            {
                var shared = active[i].Targets
                    .Select(t => t.TargetKey)
                    .Intersect(active[j].Targets.Select(t => t.TargetKey))
                    .ToList();

                if (shared.Count == 0) continue;

                report.Agreements.Add(new SignalAgreement
                {
                    SignalA = active[i].Signal,
                    SignalB = active[j].Signal,
                    SharedTargets = shared,
                    SharedTargetCount = shared.Count
                });
            }
        }

        // Stage 7b: per-target supporting signals
        foreach (var item in fused.Items)
        {
            var signals = item.Sources.Select(MapToSignal).Distinct().ToList();
            report.CorrelatedTargets.Add(new CorrelatedTarget
            {
                TargetKey = item.TargetKey,
                FusedStrength = item.FusedStrength,
                SupportingSignals = signals.OrderBy(s => s).ToList(),
                SupportingSignalCount = signals.Count
            });
        }

        return Finalize(report);
    }

    private static MultiSignalCorrelationReport Finalize(MultiSignalCorrelationReport report)
    {
        report.CorrelatedTargets = report.CorrelatedTargets
            .OrderByDescending(t => t.SupportingSignalCount)
            .ThenByDescending(t => t.FusedStrength)
            .ThenBy(t => t.TargetKey)
            .ToList();

        report.TotalTargets = report.CorrelatedTargets.Count;
        report.MultiSignalTargetCount =
            report.CorrelatedTargets.Count(t => t.SupportingSignalCount >= 2);
        report.TopTarget = report.CorrelatedTargets.FirstOrDefault();
        return report;
    }
}