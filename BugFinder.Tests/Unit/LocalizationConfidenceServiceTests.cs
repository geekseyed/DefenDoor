using System.Collections.Generic;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class LocalizationConfidenceServiceTests
{
    private static FusedEvidenceItem Item(
        string key, double strength, params CandidateEvidenceType[] sources) => new()
        {
            TargetKey = key,
            FusedStrength = strength,
            Sources = sources.ToList(),
            SignalCount = sources.Length
        };

    private static EvidenceFusionReport Fused(params FusedEvidenceItem[] items) => new()
    {
        Items = items.ToList(),
        TotalTargets = items.Length
    };

    private static EvidenceConflict Conflict(string key, ConflictSeverity severity) => new()
    {
        TargetKey = key,
        Kind = ConflictKind.Location,
        Severity = severity
    };

    // Stage 4/5 — empty report
    [Fact]
    public void Calculate_EmptyFused_ReturnsEmptyReport()
    {
        var report = new LocalizationConfidenceService().Calculate(new EvidenceFusionReport(), null);

        report.TotalTargets.Should().Be(0);
        report.TopTarget.Should().BeNull();
    }

    // Stage 1 — no conflicts: score == fused strength
    [Fact]
    public void Calculate_SingleSignalNoConflicts_ScoreEqualsFusedStrength()
    {
        var fused = Fused(Item("T1", 0.8, CandidateEvidenceType.Stack));

        var report = new LocalizationConfidenceService().Calculate(fused, null);

        var t = report.Targets[0];
        t.AgreementBoost.Should().Be(1.0);
        t.ConflictPenalty.Should().Be(0.0);
        t.ConfidenceScore.Should().BeApproximately(0.8, 0.001);
        t.Level.Should().Be(ConfidenceLevel.VeryHigh);
        report.HighConfidenceCount.Should().Be(1);
    }

    // Stage 2 — agreement boost per extra signal (capped)
    [Fact]
    public void Calculate_MultipleSignals_BoostApplied()
    {
        var fused = Fused(Item("T1", 0.5,
            CandidateEvidenceType.Stack, CandidateEvidenceType.Coverage, CandidateEvidenceType.Runtime));

        var report = new LocalizationConfidenceService().Calculate(fused, null);

        var t = report.Targets[0];
        t.SupportingSignalCount.Should().Be(3);
        t.AgreementBoost.Should().Be(1.2);                       // 1 + 2*0.1
        t.ConfidenceScore.Should().BeApproximately(0.6, 0.001);  // 0.5 * 1.2
        t.Level.Should().Be(ConfidenceLevel.High);
    }

    // Stage 3 — High conflict penalty drops a level
    [Fact]
    public void Calculate_HighConflict_PenaltyApplied()
    {
        var fused = Fused(Item("T1", 0.8, CandidateEvidenceType.Stack));
        var conflicts = new ConflictAnalysisReport
        {
            Conflicts = new List<EvidenceConflict> { Conflict("T1", ConflictSeverity.High) }
        };

        var report = new LocalizationConfidenceService().Calculate(fused, conflicts);

        var t = report.Targets[0];
        t.ConflictPenalty.Should().Be(0.40);
        t.ConfidenceScore.Should().BeApproximately(0.4, 0.001);  // 0.8 - 0.4
        t.Level.Should().Be(ConfidenceLevel.Medium);
    }

    // Stage 4 — penalties combine (Medium + Low)
    [Fact]
    public void Calculate_MixedConflicts_PenaltiesCombine()
    {
        var fused = Fused(Item("T1", 0.9,
            CandidateEvidenceType.Stack, CandidateEvidenceType.Coverage));
        var conflicts = new ConflictAnalysisReport
        {
            Conflicts = new List<EvidenceConflict>
            {
                Conflict("T1", ConflictSeverity.Medium),
                Conflict("T1", ConflictSeverity.Low)
            }
        };

        var report = new LocalizationConfidenceService().Calculate(fused, conflicts);

        var t = report.Targets[0];
        t.ConflictPenalty.Should().BeApproximately(0.25, 0.001); // 0.20 + 0.05
        t.ConfidenceScore.Should().BeApproximately(0.74, 0.001); // 0.9*1.1 - 0.25
        t.Level.Should().Be(ConfidenceLevel.High);
    }

    // Stage 4 — clamped at 1.0
    [Fact]
    public void Calculate_FullSpectrum_ClampedAtOne()
    {
        var fused = Fused(Item("Hot", 0.9,
            CandidateEvidenceType.TestFailure, CandidateEvidenceType.Stack,
            CandidateEvidenceType.Coverage, CandidateEvidenceType.Runtime,
            CandidateEvidenceType.Regression, CandidateEvidenceType.Historical,
            CandidateEvidenceType.StaticCorrelation));

        var report = new LocalizationConfidenceService().Calculate(fused, null);

        report.Targets[0].AgreementBoost.Should().Be(1.3);       // cap
        report.Targets[0].ConfidenceScore.Should().Be(1.0);      // 0.9*1.3=1.17 -> clamp
        report.Targets[0].Level.Should().Be(ConfidenceLevel.VeryHigh);
    }

    // Stage 4 — clamped at 0.0 (weak evidence, heavy conflicts)
    [Fact]
    public void Calculate_HeavyPenaltiesOnWeakEvidence_ClampedAtZero()
    {
        var fused = Fused(Item("Weak", 0.3, CandidateEvidenceType.Stack));
        var conflicts = new ConflictAnalysisReport
        {
            Conflicts = new List<EvidenceConflict>
            {
                Conflict("Weak", ConflictSeverity.High),
                Conflict("Weak", ConflictSeverity.High)
            }
        };

        var report = new LocalizationConfidenceService().Calculate(fused, conflicts);

        report.Targets[0].ConfidenceScore.Should().Be(0.0);      // 0.3-0.8 -> clamp
        report.Targets[0].Level.Should().Be(ConfidenceLevel.VeryLow);
        report.LowConfidenceCount.Should().Be(1);
    }

    // Ordering + top target
    [Fact]
    public void Calculate_OrdersByScore_AndSelectsTop()
    {
        var fused = Fused(
            Item("Low", 0.3, CandidateEvidenceType.Stack),
            Item("High", 0.9, CandidateEvidenceType.Stack));

        var report = new LocalizationConfidenceService().Calculate(fused, null);

        report.Targets[0].TargetKey.Should().Be("High");
        report.TopTarget!.TargetKey.Should().Be("High");
    }

    // Disclaimer contract
    [Fact]
    public void Report_CarriesDisclaimer()
    {
        LocalizationConfidenceReport.Disclaimer.Should().Contain("Confidence != Root Cause");
    }
}