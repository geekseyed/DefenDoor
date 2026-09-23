using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class EvidenceFusionServiceTests
{
    // Pure model logic — no Roslyn workspace needed

    // Stage 1/2
    [Fact]
    public void Fuse_EmptyInput_ReturnsEmptyReport()
    {
        var service = new EvidenceFusionService();

        var report = service.Fuse(new List<FusionEvidenceInput>());

        report.Items.Should().BeEmpty();
        report.TotalInputs.Should().Be(0);
        report.TotalTargets.Should().Be(0);
    }

    // Stage 2
    [Fact]
    public void Normalize_ClampsStrengthToUnitRange()
    {
        var service = new EvidenceFusionService();

        var normalized = service.Normalize(new[]
        {
            new FusionEvidenceInput { SourceType = CandidateEvidenceType.Stack, RawStrength = -0.5, TargetSymbolKey = "K1" },
            new FusionEvidenceInput { SourceType = CandidateEvidenceType.Coverage, RawStrength = 2.5, TargetSymbolKey = "K2" }
        });

        normalized[0].RawStrength.Should().Be(0.0);
        normalized[1].RawStrength.Should().Be(1.0);
    }

    // Stage 4 — weighted fusion + corroboration
    [Fact]
    public void Fuse_SameSymbolMultipleSources_WeightedAverageAndCorroboration()
    {
        var service = new EvidenceFusionService();

        var report = service.Fuse(new[]
        {
            new FusionEvidenceInput { SourceType = CandidateEvidenceType.Stack, RawStrength = 0.8, TargetSymbolKey = "M|R|Compute()" },
            new FusionEvidenceInput { SourceType = CandidateEvidenceType.Coverage, RawStrength = 0.4, TargetSymbolKey = "M|R|Compute()" }
        });

        report.Items.Should().HaveCount(1);
        var item = report.Items[0];
        item.FusedStrength.Should().BeApproximately(0.6, 0.001);   // (0.8+0.4)/2
        item.MaxSingleStrength.Should().Be(0.8);
        item.SignalCount.Should().Be(2);
        item.DistinctSourceCount.Should().Be(2);
        item.Sources.Should().Contain(CandidateEvidenceType.Stack)
            .And.Contain(CandidateEvidenceType.Coverage);
        report.FullyCorroboratedCount.Should().Be(1);
        report.TotalTargets.Should().Be(1);
    }

    // Grouping precedence — symbol key beats file
    [Fact]
    public void Fuse_SymbolKeyTakesPrecedenceOverFileForGrouping()
    {
        var service = new EvidenceFusionService();

        var report = service.Fuse(new[]
        {
            new FusionEvidenceInput { SourceType = CandidateEvidenceType.Stack, RawStrength = 0.9, TargetSymbolKey = "M|R|Compute()", TargetFilePath = "a.cs" },
            new FusionEvidenceInput { SourceType = CandidateEvidenceType.Historical, RawStrength = 0.3, TargetSymbolKey = "M|R|Compute()", TargetFilePath = "b.cs" }
        });

        report.Items.Should().HaveCount(1);
        report.Items[0].TargetKey.Should().Be("M|R|Compute()");
        report.Items[0].FusedStrength.Should().BeApproximately(0.6, 0.001);
    }

    // Stage 3 — configurable source weights
    [Fact]
    public void Fuse_ConfigurableSourceWeights_ShiftFusedStrength()
    {
        var config = new EvidenceWeightConfig();
        config.SetWeight(CandidateEvidenceType.Stack, 3.0);
        config.SetWeight(CandidateEvidenceType.Coverage, 1.0);
        var service = new EvidenceFusionService(config);

        var report = service.Fuse(new[]
        {
            new FusionEvidenceInput { SourceType = CandidateEvidenceType.Stack, RawStrength = 0.8, TargetSymbolKey = "K" },
            new FusionEvidenceInput { SourceType = CandidateEvidenceType.Coverage, RawStrength = 0.4, TargetSymbolKey = "K" }
        });

        // (3*0.8 + 1*0.4) / 4 = 0.7
        report.Items[0].FusedStrength.Should().BeApproximately(0.7, 0.001);
    }

    // FILE-level grouping when no symbol key (uses new enum members)
    [Fact]
    public void Fuse_FileOnlyGrouping_WhenNoSymbolKey()
    {
        var service = new EvidenceFusionService();

        var report = service.Fuse(new[]
        {
            new FusionEvidenceInput { SourceType = CandidateEvidenceType.Regression, RawStrength = 0.5, TargetFilePath = "svc.cs" },
            new FusionEvidenceInput { SourceType = CandidateEvidenceType.TestFailure, RawStrength = 1.0, TargetFilePath = "svc.cs", TargetLineNumber = 10 }
        });

        report.Items.Should().HaveCount(1);
        report.Items[0].TargetKey.Should().Be("FILE|svc.cs");
        report.Items[0].SignalCount.Should().Be(2);
    }

    // Adapter — BF-12.10 (no fabrication for evidence-less candidates)
    [Fact]
    public void FromRankedResult_ConvertsOnlyEvidenceBearingCandidates()
    {
        var ranked = new RankedLocalizationResult
        {
            Candidates = new List<EvidenceRankedCandidate>
            {
                new EvidenceRankedCandidate
                {
                    ElementId = "C1", FilePath = "calc.cs", Rank = 1, UnifiedScore = 0.9,
                    Evidence = new List<CandidateEvidenceRef>
                    {
                        new CandidateEvidenceRef { Type = CandidateEvidenceType.Stack, Strength = 0.9, SourceArtifact = "bug.trx" },
                        new CandidateEvidenceRef { Type = CandidateEvidenceType.Coverage, Strength = 0.5, SourceArtifact = "cov.xml" }
                    }
                },
                new EvidenceRankedCandidate { ElementId = "C2", FilePath = "log.cs", Rank = 2, UnifiedScore = 0.2 } // no evidence
            }
        };

        var service = new EvidenceFusionService();

        var inputs = service.FromRankedResult(ranked);
        inputs.Should().HaveCount(2); // C2 contributes nothing
        inputs.Select(i => i.SourceArtifact).Should().Contain("bug.trx").And.Contain("cov.xml");

        var report = service.Fuse(inputs);
        report.TotalInputs.Should().Be(2);
        report.Items.Should().HaveCount(1); // same FILE|calc.cs target
        report.Items[0].FusedStrength.Should().BeApproximately(0.7, 0.001); // (0.9+0.5)/2
    }

    // Adapter — BF-13.8 (scaled by match confidence; uncorrelated skipped)
    [Fact]
    public void FromCorrelationReport_ScalesByMatchConfidence_SkipsUncorrelated()
    {
        var correlation = new StaticDynamicCorrelationReport
        {
            Items = new List<CorrelatedCodeEvidence>
            {
                new CorrelatedCodeEvidence
                {
                    IsCorrelated = true, Strategy = CorrelationMatchStrategy.MethodNameAndFile, MatchConfidence = 1.0,
                    PrimarySymbolKey = "M|R|Compute()",
                    Dynamic = new DynamicLocationEvidence { SourceType = DynamicEvidenceSource.Stack, SignalStrength = 0.8, SourceArtifact = "frame#1" }
                },
                new CorrelatedCodeEvidence
                {
                    IsCorrelated = false, Strategy = CorrelationMatchStrategy.None, MatchConfidence = 0.0,
                    Dynamic = new DynamicLocationEvidence { SourceType = DynamicEvidenceSource.Coverage, SignalStrength = 0.9, FilePath = "ghost.cs" }
                },
                new CorrelatedCodeEvidence
                {
                    IsCorrelated = true, Strategy = CorrelationMatchStrategy.FileOnly, MatchConfidence = 0.4,
                    PrimarySymbolKey = "M|R|Save()",
                    Dynamic = new DynamicLocationEvidence { SourceType = DynamicEvidenceSource.Runtime, SignalStrength = 0.5 }
                }
            }
        };

        var service = new EvidenceFusionService();

        var inputs = service.FromCorrelationReport(correlation);
        inputs.Should().HaveCount(2);                    // uncorrelated skipped (UNKNOWN)
        inputs[0].RawStrength.Should().BeApproximately(0.8, 0.001);  // 0.8 * 1.0
        inputs[1].RawStrength.Should().BeApproximately(0.2, 0.001);  // 0.5 * 0.4

        var report = service.Fuse(inputs);
        report.Items[0].FusedStrength.Should().BeApproximately(0.8, 0.001); // sorted desc
        report.Items[0].TargetSymbolKey.Should().Be("M|R|Compute()");
    }
}