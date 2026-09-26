using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class InvestigationChainServiceTests
{
    private readonly InvestigationChainService _service = new();

    private static readonly FailureIdentityInput Failure = new()
    {
        TestIdentity = "ISCM.Tests.EventLogSizeCheck_Test",
        EvaluationIdentity = "EVL-001.4",
        FailureCategory = "Assertion:Equality",
        FailureSignature = "sig-abc-123"
    };

    private static readonly List<FailureLocation> Locations = new()
    {
        new FailureLocation { FilePath = "Check.cs", LineNumber = 42, MethodName = "Evaluate", IsPrimary = true },
        new FailureLocation { FilePath = "Helper.cs", LineNumber = 10, MethodName = "Compare" }
    };

    private static EvidenceFusionReport Fused(int inputs = 3, int targets = 2, int corroborated = 1) => new()
    {
        TotalInputs = inputs,
        TotalTargets = targets,
        FullyCorroboratedCount = corroborated,
        Items = new List<FusedEvidenceItem>()
    };

    private static MultiSignalCorrelationReport Correlated(params (string Key, int Signals)[] targets) => new()
    {
        TotalTargets = targets.Length,
        MultiSignalTargetCount = targets.Count(t => t.Signals >= 2),
        Agreements = new List<SignalAgreement> { new SignalAgreement() },
        CorrelatedTargets = targets.Select(t => new CorrelatedTarget
        {
            TargetKey = t.Key,
            SupportingSignalCount = t.Signals,
            FusedStrength = 0.5 + t.Signals * 0.1,
            SupportingSignals = Enumerable.Range(0, t.Signals)
        .Select(i => (InvestigationSignal)i)
        .ToList()
        }).ToList()
    };

    // Stage 7 — full pipeline closes
    [Fact]
    public void Build_FullPipeline_ProducesCompleteChain()
    {
        var report = _service.Build(Failure, Locations, Fused(), Correlated(("T1", 3), ("T2", 1)));

        report.IsComplete.Should().BeTrue();
        report.Steps.Should().OnlyContain(s => s.Status == InvestigationStageStatus.Complete);
        report.Failure.Should().BeSameAs(Failure);
    }

    // Canonical order = enum order
    [Fact]
    public void Build_StepsInCanonicalOrder()
    {
        var report = _service.Build(Failure, Locations, Fused(), Correlated(("T1", 2)));

        report.Steps.Select(s => s.Kind).Should().ContainInOrder(
            InvestigationStageKind.Failure,
            InvestigationStageKind.Classification,
            InvestigationStageKind.Localization,
            InvestigationStageKind.Evidence,
            InvestigationStageKind.Correlation,
            InvestigationStageKind.SuspiciousLocations,
            InvestigationStageKind.Chain);
    }

    // Missing correlation -> suspicious empty, chain partial
    [Fact]
    public void Build_MissingCorrelation_SuspiciousEmptyAndChainPartial()
    {
        var report = _service.Build(Failure, Locations, Fused(), null);

        report.IsComplete.Should().BeFalse();
        report.Steps.First(s => s.Kind == InvestigationStageKind.Correlation)
            .Status.Should().Be(InvestigationStageStatus.Empty);
        report.Steps.First(s => s.Kind == InvestigationStageKind.SuspiciousLocations)
            .Status.Should().Be(InvestigationStageStatus.Empty);
        report.SuspiciousLocations.Should().BeEmpty();
    }

    // Nothing provided -> failure/classification/evidence empty, never throws
    [Fact]
    public void Build_NoInputs_GracefulEmptyChain()
    {
        var report = _service.Build(null, null, null, null);

        report.IsComplete.Should().BeFalse();
        report.Steps.First(s => s.Kind == InvestigationStageKind.Failure)
            .Status.Should().Be(InvestigationStageStatus.Empty);
        report.Steps.First(s => s.Kind == InvestigationStageKind.Classification)
            .Status.Should().Be(InvestigationStageStatus.Empty);
        report.Steps.First(s => s.Kind == InvestigationStageKind.Evidence)
            .Status.Should().Be(InvestigationStageStatus.Empty);
    }

    // Stage 6 — top-N ordering preserved from 14.2, limit applied
    [Fact]
    public void Build_SuspiciousLocations_TakeTopN_InCorrelationOrder()
    {
        var correlation = Correlated(("First", 3), ("Second", 2), ("Third", 1));

        var report = _service.Build(Failure, Locations, Fused(), correlation, maxSuspiciousLocations: 2);

        report.SuspiciousLocations.Should().HaveCount(2);
        report.SuspiciousLocations[0].TargetKey.Should().Be("First");
        report.SuspiciousLocations[1].TargetKey.Should().Be("Second");
        report.SuspiciousLocations[0].SupportingSignals.Should().HaveCount(3);
    }

    // Partial degradations: unclassified + no primary + single-signal only
    [Fact]
    public void Build_PartialInputs_FlagsPartialStatuses()
    {
        var failure = new FailureIdentityInput { TestIdentity = "Some_Test" };      // no category
        var locations = new List<FailureLocation>                                    // no primary
            { new FailureLocation { FilePath = "a.cs", LineNumber = 1 } };
        var fused = Fused(inputs: 2, targets: 1, corroborated: 0);                   // no corroboration
        var correlated = Correlated(("Solo", 1));                                    // single-signal

        var report = _service.Build(failure, locations, fused, correlated);

        report.Steps.First(s => s.Kind == InvestigationStageKind.Classification)
            .Status.Should().Be(InvestigationStageStatus.Partial);
        report.Steps.First(s => s.Kind == InvestigationStageKind.Localization)
            .Status.Should().Be(InvestigationStageStatus.Partial);
        report.Steps.First(s => s.Kind == InvestigationStageKind.Evidence)
            .Status.Should().Be(InvestigationStageStatus.Partial);
        report.Steps.First(s => s.Kind == InvestigationStageKind.Correlation)
            .Status.Should().Be(InvestigationStageStatus.Partial);
        report.IsComplete.Should().BeFalse();
    }

    // Disclaimer always present (suspiciousness != root cause)
    [Fact]
    public void Report_CarriesDisclaimer()
    {
        InvestigationChainReport.Disclaimer.Should().Contain("Suspiciousness != Root Cause");
    }
}