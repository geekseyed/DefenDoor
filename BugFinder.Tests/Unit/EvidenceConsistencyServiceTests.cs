using System;
using System.Collections.Generic;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class EvidenceConsistencyServiceTests
{
    private static readonly DateTime T0 = new(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);

    private static FusionEvidenceInput Input(
        CandidateEvidenceType type, string? file = null, DateTime? at = null, string? symbol = null) => new()
        {
            SourceType = type,
            RawStrength = 0.8,
            TargetSymbolKey = symbol,
            TargetFilePath = file,
            ObservedAtUtc = at
        };

    // Stage 4 — empty report
    [Fact]
    public void Evaluate_NoInputs_ReturnsNoneOverall()
    {
        var report = new EvidenceConsistencyService().Evaluate(null, null);

        report.TotalTargets.Should().Be(0);
        report.OverallStatus.Should().Be(ReportConsistencyStatus.None);
    }

    // Stages 1-4 — corroborated + aligned (case-insensitive) + coherent
    [Fact]
    public void Evaluate_CorroboratedAlignedCoherent_TargetConsistent()
    {
        var inputs = new[]
        {
            Input(CandidateEvidenceType.Stack,    file: "Calc.cs", at: T0,               symbol: "M|R|Compute()"),
            Input(CandidateEvidenceType.Coverage, file: "calc.cs", at: T0.AddHours(2),   symbol: "M|R|Compute()")
        };
        var fused = new EvidenceFusionService().Fuse(inputs);

        var report = new EvidenceConsistencyService().Evaluate(inputs, fused);

        report.TotalTargets.Should().Be(1);
        var target = report.Targets[0];
        target.CrossSource.Should().Be(CrossSourceStatus.Corroborated);
        target.Location.Should().Be(LocationStatus.Aligned);        // case-insensitive match
        target.Timeline.Should().Be(TimelineStatus.Coherent);
        target.TimelineSpread.Should().Be(TimeSpan.FromHours(2));
        target.Verdict.Should().Be(TargetConsistencyVerdict.Consistent);
        report.OverallStatus.Should().Be(ReportConsistencyStatus.Consistent);
    }

    // Stage 1 — single source weakens to Partial
    [Fact]
    public void Evaluate_SingleSource_TargetPartial()
    {
        var inputs = new[] { Input(CandidateEvidenceType.Stack, file: "a.cs", symbol: "M|R|Only()") };
        var fused = new EvidenceFusionService().Fuse(inputs);

        var report = new EvidenceConsistencyService().Evaluate(inputs, fused);

        report.Targets[0].Verdict.Should().Be(TargetConsistencyVerdict.Partial);
        report.OverallStatus.Should().Be(ReportConsistencyStatus.Partial);
    }

    // Stage 2 — location divergence = hard inconsistency
    [Fact]
    public void Evaluate_DivergentLocations_TargetInconsistent()
    {
        var inputs = new[]
        {
            Input(CandidateEvidenceType.Stack,    file: "Calc.cs", symbol: "M|R|Split()"),
            Input(CandidateEvidenceType.Historical, file: "Other.cs", symbol: "M|R|Split()")
        };
        var fused = new EvidenceFusionService().Fuse(inputs);

        var report = new EvidenceConsistencyService().Evaluate(inputs, fused);

        var target = report.Targets[0];
        target.Location.Should().Be(LocationStatus.Divergent);
        target.DistinctLocations.Should().HaveCount(2);
        target.Verdict.Should().Be(TargetConsistencyVerdict.Inconsistent);
        report.InconsistentCount.Should().Be(1);
        report.OverallStatus.Should().Be(ReportConsistencyStatus.Inconsistent);
    }

    // Stage 3 — timeline beyond default tolerance weakens to Partial
    [Fact]
    public void Evaluate_TimelineDispersed_WeakensToPartial()
    {
        var inputs = new[]
        {
            Input(CandidateEvidenceType.Stack,    file: "a.cs", at: T0,             symbol: "M|R|Old()"),
            Input(CandidateEvidenceType.Coverage, file: "a.cs", at: T0.AddHours(48), symbol: "M|R|Old()")
        };
        var fused = new EvidenceFusionService().Fuse(inputs);

        var report = new EvidenceConsistencyService().Evaluate(inputs, fused);

        var target = report.Targets[0];
        target.CrossSource.Should().Be(CrossSourceStatus.Corroborated);   // sources agree on WHAT
        target.Timeline.Should().Be(TimelineStatus.Dispersed);            // but not on WHEN
        target.Verdict.Should().Be(TargetConsistencyVerdict.Partial);
    }

    // Stage 3 — configurable tolerance
    [Fact]
    public void Evaluate_CustomTolerance_MakesDispersedCoherent()
    {
        var inputs = new[]
        {
            Input(CandidateEvidenceType.Stack,    file: "a.cs", at: T0,             symbol: "M|R|Slow()"),
            Input(CandidateEvidenceType.Coverage, file: "a.cs", at: T0.AddHours(48), symbol: "M|R|Slow()")
        };
        var fused = new EvidenceFusionService().Fuse(inputs);

        var report = new EvidenceConsistencyService(TimeSpan.FromHours(72)).Evaluate(inputs, fused);

        report.Targets[0].Timeline.Should().Be(TimelineStatus.Coherent);
        report.Targets[0].Verdict.Should().Be(TargetConsistencyVerdict.Consistent);
    }

    // Stage 4 — mixed report + fused-only (no raw inputs) = Unknown
    [Fact]
    public void Evaluate_MixedReport_CountsAndOverallPartial()
    {
        var raw = new[]
        {
            Input(CandidateEvidenceType.Stack,    file: "a.cs", at: T0, symbol: "M|R|A()"),
            Input(CandidateEvidenceType.Coverage, file: "a.cs", at: T0.AddHours(1), symbol: "M|R|A()"),
            Input(CandidateEvidenceType.Stack,    file: "b.cs", at: T0, symbol: "M|R|B()")
        };
        var fused = new EvidenceFusionReport
        {
            Items = new List<FusedEvidenceItem>
            {
                new() { TargetKey = "M|R|A()", FusedStrength = 0.9, DistinctSourceCount = 2, Sources = new List<CandidateEvidenceType> { CandidateEvidenceType.Stack, CandidateEvidenceType.Coverage } },
                new() { TargetKey = "M|R|B()", FusedStrength = 0.5, DistinctSourceCount = 1, Sources = new List<CandidateEvidenceType> { CandidateEvidenceType.Stack } },
                new() { TargetKey = "M|R|Ghost()", FusedStrength = 0.7, DistinctSourceCount = 2, Sources = new List<CandidateEvidenceType> { CandidateEvidenceType.Regression, CandidateEvidenceType.Historical } }
            }
        };

        var report = new EvidenceConsistencyService().Evaluate(raw, fused);

        report.TotalTargets.Should().Be(3);
        report.ConsistentCount.Should().Be(1);     // A
        report.PartialCount.Should().Be(1);        // B (single source)
        report.UnknownCount.Should().Be(1);        // Ghost (no raw inputs)
        report.InconsistentCount.Should().Be(0);
        report.OverallStatus.Should().Be(ReportConsistencyStatus.Partial);
    }
}