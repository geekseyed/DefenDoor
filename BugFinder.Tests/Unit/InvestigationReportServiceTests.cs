using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class InvestigationReportServiceTests
{
    private readonly InvestigationReportService _service = new();
    private static readonly DateTime T0 = new(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);

    private static readonly FailureIdentityInput Failure = new()
    {
        TestIdentity = "ISCM.Tests.EventLogSizeCheck_Test",
        EvaluationIdentity = "EVL-001.4",
        FailureCategory = "Assertion:Equality",
        FailureSignature = "sig-abc"
    };

    private static readonly List<FailureLocation> PrimaryLocation = new()
    {
        new() { FilePath = "Check.cs", LineNumber = 42, MethodName = "Evaluate", IsPrimary = true }
    };

    private static FusedEvidenceItem Item(
        string key, double strength, params CandidateEvidenceType[] sources) => new()
        {
            TargetKey = key,
            FusedStrength = strength,
            Sources = sources.ToList(),
            DistinctSourceCount = sources.Length,
            SignalCount = sources.Length
        };

    // Assemble — full synthetic input -> Complete + timeline
    [Fact]
    public void Assemble_FullInput_OutcomeCompleteWithTimeline()
    {
        var fused = new EvidenceFusionReport
        {
            TotalInputs = 3,
            TotalTargets = 1,
            FullyCorroboratedCount = 1,
            Items = new List<FusedEvidenceItem> { Item("T1", 0.8, CandidateEvidenceType.Stack, CandidateEvidenceType.Coverage) }
        };
        var correlation = new MultiSignalCorrelationReport
        {
            TotalTargets = 1,
            MultiSignalTargetCount = 1,
            Agreements = new List<SignalAgreement> { new() },
            CorrelatedTargets = new List<CorrelatedTarget>
            {
                new() { TargetKey = "T1", FusedStrength = 0.8, SupportingSignalCount = 2 }
            }
        };
        var chain = new InvestigationChainService().Build(Failure, PrimaryLocation, fused, correlation);

        var report = _service.Assemble(new InvestigationReportInput
        {
            Failure = Failure,
            Locations = PrimaryLocation,
            FusedEvidence = fused,
            Correlation = correlation,
            Chain = chain
        });

        report.Outcome.Should().Be(InvestigationOutcome.Complete);
        report.Timeline.Should().HaveCount(7);
        report.SuspiciousLocations.Should().ContainSingle().Which.TargetKey.Should().Be("T1");
        report.Evidence!.TotalInputs.Should().Be(3);
        report.Evidence.FullyCorroboratedCount.Should().Be(1);
        report.Failure.Should().BeSameAs(Failure);
    }

    // Assemble — empty input -> InsufficientEvidence, no throw
    [Fact]
    public void Assemble_EmptyInput_InsufficientEvidence()
    {
        var report = _service.Assemble(new InvestigationReportInput());

        report.Outcome.Should().Be(InvestigationOutcome.InsufficientEvidence);
        report.Timeline.Should().BeEmpty();
        report.SuspiciousLocations.Should().BeEmpty();
        report.Evidence.Should().BeNull();
    }

    // Assemble — no chain -> Partial + correlation fallback for suspicious
    [Fact]
    public void Assemble_NoChain_PartialWithCorrelationFallback()
    {
        var input = new InvestigationReportInput
        {
            FusedEvidence = new EvidenceFusionReport
            {
                TotalInputs = 2,
                TotalTargets = 3,
                Items = new List<FusedEvidenceItem> { Item("X", 0.5, CandidateEvidenceType.Stack) }
            },
            Correlation = new MultiSignalCorrelationReport
            {
                TotalTargets = 3,
                CorrelatedTargets = new List<CorrelatedTarget>
                {
                    new() { TargetKey = "A", SupportingSignalCount = 2 },
                    new() { TargetKey = "B", SupportingSignalCount = 2 },
                    new() { TargetKey = "C", SupportingSignalCount = 1 }
                }
            },
            MaxSuspiciousLocations = 2
        };

        var report = _service.Assemble(input);

        report.Outcome.Should().Be(InvestigationOutcome.Partial);
        report.Timeline.Should().BeEmpty();                       // no chain -> no timeline
        report.SuspiciousLocations.Select(s => s.TargetKey)
            .Should().ContainInOrder("A", "B");                   // fallback top-N
    }

    // Assemble — evidence summary extraction from consistency + conflicts
    [Fact]
    public void Assemble_EvidenceSummary_ExtractedFromSubReports()
    {
        var input = new InvestigationReportInput
        {
            FusedEvidence = new EvidenceFusionReport
            {
                TotalInputs = 2,
                TotalTargets = 1,
                FullyCorroboratedCount = 1
            },
            Consistency = new EvidenceConsistencyReport
            {
                OverallStatus = ReportConsistencyStatus.Inconsistent
            },
            Conflicts = new ConflictAnalysisReport
            {
                UnresolvedCount = 2,
                Conflicts = new List<EvidenceConflict>
                {
                    new(), new()
                }
            }
        };

        var report = _service.Assemble(input);

        report.Evidence!.ConsistencyStatus.Should().Be(ReportConsistencyStatus.Inconsistent);
        report.Evidence.ConflictCount.Should().Be(2);
        report.Evidence.UnresolvedConflictCount.Should().Be(2);
    }

    // Investigate — clean corroborated end-to-end -> Complete
    [Fact]
    public void Investigate_CleanCorroboratedEvidence_CompleteOutcome()
    {
        var raw = new[]
        {
            new FusionEvidenceInput { SourceType = CandidateEvidenceType.Stack,    RawStrength = 0.9, TargetSymbolKey = "M|ISCM|Check.Evaluate()", TargetFilePath = "Check.cs", ObservedAtUtc = T0 },
            new FusionEvidenceInput { SourceType = CandidateEvidenceType.Coverage, RawStrength = 0.7, TargetSymbolKey = "M|ISCM|Check.Evaluate()", TargetFilePath = "Check.cs", ObservedAtUtc = T0.AddMinutes(5) }
        };

        var report = _service.Investigate(Failure, PrimaryLocation, raw);

        report.Outcome.Should().Be(InvestigationOutcome.Complete);
        report.Timeline.Should().HaveCount(7);
        report.SuspiciousLocations.Should().ContainSingle();
        report.Evidence!.TotalInputs.Should().Be(2);
        report.Evidence.ConsistencyStatus.Should().Be(ReportConsistencyStatus.Consistent);
        report.Evidence.ConflictCount.Should().Be(0);

        // fused (0.9+0.7)/2 = 0.8; two signals -> boost 1.1 -> 0.88
        report.Confidence!.TopTarget!.ConfidenceScore.Should().BeApproximately(0.88, 0.001);
        report.Confidence.TopTarget.Level.Should().Be(ConfidenceLevel.VeryHigh);
        report.Uncertainty!.Targets[0].Level.Should().Be(UncertaintyLevel.Low);
    }

    // Investigate — divergent locations: conflicts inform, chain still completes
    [Fact]
    public void Investigate_DivergentLocations_ConflictsRecordedConfidencePenalized()
    {
        var raw = new[]
        {
            new FusionEvidenceInput { SourceType = CandidateEvidenceType.Stack,      RawStrength = 0.9, TargetSymbolKey = "M|ISCM|Split()", TargetFilePath = "Calc.cs",  ObservedAtUtc = T0 },
            new FusionEvidenceInput { SourceType = CandidateEvidenceType.Historical, RawStrength = 0.6, TargetSymbolKey = "M|ISCM|Split()", TargetFilePath = "Other.cs", ObservedAtUtc = T0.AddHours(120) }
        };

        var report = _service.Investigate(Failure, PrimaryLocation, raw);

        // conflicts do not break the chain - they inform it
        report.Outcome.Should().Be(InvestigationOutcome.Complete);
        report.Evidence!.ConflictCount.Should().Be(2);            // Location High + Timeline Medium
        report.Conflicts!.HighSeverityCount.Should().Be(1);
        report.Conflicts.UnresolvedCount.Should().Be(2);          // preserved honestly

        // confidence: 0.75 * 1.1 - (0.40 + 0.20) = 0.225 -> Low
        report.Confidence!.Targets[0].ConfidenceScore.Should().BeApproximately(0.225, 0.001);
        report.Confidence.Targets[0].Level.Should().Be(ConfidenceLevel.Low);

        // uncertainty: 0.2 (High conflict) + 0.1 (Medium conflict) = 0.3 -> Medium
        report.Uncertainty!.Targets[0].UncertaintyScore.Should().BeApproximately(0.3, 0.001);
        report.Uncertainty.Targets[0].Level.Should().Be(UncertaintyLevel.Medium);
    }

    // Investigate — failure only -> Partial, no suspicious, no evidence summary
    [Fact]
    public void Investigate_FailureOnly_PartialOutcome()
    {
        var report = _service.Investigate(Failure, null, null);

        report.Outcome.Should().Be(InvestigationOutcome.Partial);
        report.SuspiciousLocations.Should().BeEmpty();
        report.Evidence.Should().BeNull();
        report.Timeline.Should().HaveCount(7);
    }

    // Investigate — nothing at all -> InsufficientEvidence
    [Fact]
    public void Investigate_Nothing_InsufficientEvidence()
    {
        var report = _service.Investigate(null, null, null);

        report.Outcome.Should().Be(InvestigationOutcome.InsufficientEvidence);
        report.Timeline.Should().BeEmpty();
    }

    // Investigate — suspicious top-N respected across multiple targets
    [Fact]
    public void Investigate_MultipleTargets_TopNRespectedInOrder()
    {
        var raw = new[]
        {
            new FusionEvidenceInput { SourceType = CandidateEvidenceType.Stack,      RawStrength = 0.9, TargetSymbolKey = "T1", TargetFilePath = "a.cs" },
            new FusionEvidenceInput { SourceType = CandidateEvidenceType.Coverage,   RawStrength = 0.7, TargetSymbolKey = "T1", TargetFilePath = "a.cs" },
            new FusionEvidenceInput { SourceType = CandidateEvidenceType.Stack,      RawStrength = 0.5, TargetSymbolKey = "T2", TargetFilePath = "b.cs" },
            new FusionEvidenceInput { SourceType = CandidateEvidenceType.Runtime,    RawStrength = 0.8, TargetSymbolKey = "T3", TargetFilePath = "c.cs" },
            new FusionEvidenceInput { SourceType = CandidateEvidenceType.Historical, RawStrength = 0.6, TargetSymbolKey = "T3", TargetFilePath = "c.cs" }
        };

        var report = _service.Investigate(Failure, PrimaryLocation, raw, maxSuspiciousLocations: 2);

        report.SuspiciousLocations.Should().HaveCount(2);
        // both T1 and T3 have 2 signals; T1 fused 0.8 > T3 fused 0.7
        report.SuspiciousLocations[0].TargetKey.Should().Be("T1");
        report.SuspiciousLocations[1].TargetKey.Should().Be("T3");
    }

    // Contract — disclaimers + core boundary
    [Fact]
    public void Report_CarriesDisclaimersAndCoreBoundary()
    {
        InvestigationReport.DisclaimerSuspiciousness.Should().Contain("Suspiciousness != Root Cause");
        InvestigationReport.DisclaimerConfidence.Should().Contain("Confidence != Root Cause");
        InvestigationReport.DisclaimerUncertainty.Should().Contain("Missing Evidence != Failure");
        InvestigationReport.CoreBoundary.Should().Contain("CORE STOPS HERE");
        InvestigationReport.CoreBoundary.Should().Contain("No patch");
    }
}