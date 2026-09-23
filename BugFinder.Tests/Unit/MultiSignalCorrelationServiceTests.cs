using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class MultiSignalCorrelationServiceTests
{
    private readonly MultiSignalCorrelationService _service = new();

    private static FusedEvidenceItem Item(
        string key, double strength, params CandidateEvidenceType[] sources) => new()
        {
            TargetKey = key,
            FusedStrength = strength,
            Sources = sources.ToList()
        };

    // Empty input
    [Fact]
    public void Correlate_EmptyFusedReport_ReturnsEmptyCorrelation()
    {
        var report = _service.Correlate(new EvidenceFusionReport());

        report.Profiles.Should().HaveCount(7);          // all signals profiled, all empty
        report.Profiles.Should().OnlyContain(p => p.TargetCount == 0);
        report.Agreements.Should().BeEmpty();
        report.CorrelatedTargets.Should().BeEmpty();
        report.TopTarget.Should().BeNull();
    }

    // Stage 1 mapping — all test-derived sources collapse into Test signal
    [Fact]
    public void MapToSignal_TestDerivedSources_AllMapToTestSignal()
    {
        MultiSignalCorrelationService.MapToSignal(CandidateEvidenceType.TestFailure)
            .Should().Be(InvestigationSignal.Test);
        MultiSignalCorrelationService.MapToSignal(CandidateEvidenceType.DomainFailure)
            .Should().Be(InvestigationSignal.Test);
        MultiSignalCorrelationService.MapToSignal(CandidateEvidenceType.Sbfl)
            .Should().Be(InvestigationSignal.Test);
        MultiSignalCorrelationService.MapToSignal(CandidateEvidenceType.MultiTest)
            .Should().Be(InvestigationSignal.Test);
        MultiSignalCorrelationService.MapToSignal(CandidateEvidenceType.StaticCorrelation)
            .Should().Be(InvestigationSignal.Static);
    }

    // Stage 7 — shared target produces pairwise agreement
    [Fact]
    public void Correlate_SharedTarget_ProducesAgreementAndTopTarget()
    {
        var fused = new EvidenceFusionReport
        {
            Items = new List<FusedEvidenceItem>
            {
                Item("T1", 0.8, CandidateEvidenceType.Stack, CandidateEvidenceType.Coverage),
                Item("T2", 0.6, CandidateEvidenceType.Stack),
                Item("T3", 0.5, CandidateEvidenceType.Runtime)
            }
        };

        var report = _service.Correlate(fused);

        // profiles
        var stack = report.Profiles.First(p => p.Signal == InvestigationSignal.Stack);
        stack.TargetCount.Should().Be(2);
        report.Profiles.First(p => p.Signal == InvestigationSignal.Coverage).TargetCount.Should().Be(1);
        report.Profiles.First(p => p.Signal == InvestigationSignal.Runtime).TargetCount.Should().Be(1);
        report.Profiles.First(p => p.Signal == InvestigationSignal.Test).TargetCount.Should().Be(0);

        // agreement: Stack ∩ Coverage = {T1}
        report.Agreements.Should().HaveCount(1);
        var agreement = report.Agreements[0];
        agreement.SharedTargetCount.Should().Be(1);
        agreement.SharedTargets.Should().Contain("T1");
        agreement.SignalA.Should().Be(InvestigationSignal.Stack);
        agreement.SignalB.Should().Be(InvestigationSignal.Coverage);

        // correlated targets
        report.TotalTargets.Should().Be(3);
        report.MultiSignalTargetCount.Should().Be(1);
        report.TopTarget!.TargetKey.Should().Be("T1");
        report.TopTarget.SupportingSignalCount.Should().Be(2);
    }

    // Disjoint signals — no agreement, no multi-signal target
    [Fact]
    public void Correlate_DisjointSignals_NoAgreements()
    {
        var fused = new EvidenceFusionReport
        {
            Items = new List<FusedEvidenceItem>
            {
                Item("A", 0.9, CandidateEvidenceType.Stack),
                Item("B", 0.8, CandidateEvidenceType.Coverage),
                Item("C", 0.7, CandidateEvidenceType.Regression)
            }
        };

        var report = _service.Correlate(fused);

        report.Agreements.Should().BeEmpty();
        report.MultiSignalTargetCount.Should().Be(0);
        report.TopTarget!.SupportingSignalCount.Should().Be(1);
    }

    // Full-spectrum support — one target backed by all 7 signals
    [Fact]
    public void Correlate_AllSignalsSupportOneTarget_FullAgreement()
    {
        var fused = new EvidenceFusionReport
        {
            Items = new List<FusedEvidenceItem>
            {
                Item("Hot", 0.95,
                    CandidateEvidenceType.TestFailure,
                    CandidateEvidenceType.Sbfl,
                    CandidateEvidenceType.Stack,
                    CandidateEvidenceType.Coverage,
                    CandidateEvidenceType.Runtime,
                    CandidateEvidenceType.Regression,
                    CandidateEvidenceType.Historical,
                    CandidateEvidenceType.StaticCorrelation)
            }
        };

        var report = _service.Correlate(fused);

        report.TopTarget!.SupportingSignalCount.Should().Be(7);
        report.TopTarget.SupportingSignals.Should().HaveCount(7);
        report.MultiSignalTargetCount.Should().Be(1);

        // every active signal agrees with every other active signal: C(7,2) = 21
        report.Agreements.Should().HaveCount(21);
        report.Agreements.Should().OnlyContain(a => a.SharedTargetCount == 1);
    }

    // Ordering — most signals first, then strongest
    [Fact]
    public void Correlate_OrdersTargets_BySignalCountThenStrength()
    {
        var fused = new EvidenceFusionReport
        {
            Items = new List<FusedEvidenceItem>
            {
                Item("Weak2", 0.3, CandidateEvidenceType.Stack, CandidateEvidenceType.Coverage),
                Item("Strong1", 0.9, CandidateEvidenceType.Stack),
                Item("Broad2", 0.5, CandidateEvidenceType.Runtime, CandidateEvidenceType.Historical)
            }
        };

        var report = _service.Correlate(fused);

        report.CorrelatedTargets.Select(t => t.TargetKey)
            .Should().ContainInOrder("Broad2", "Weak2", "Strong1"); // equal signals: higher strength first
    }
}