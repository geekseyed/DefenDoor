using System.Collections.Generic;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class UncertaintyModelServiceTests
{
    private static FusedEvidenceItem Item(
        string key, params CandidateEvidenceType[] sources) => new()
        {
            TargetKey = key,
            FusedStrength = 0.7,
            Sources = sources.ToList(),
            DistinctSourceCount = sources.Length,
            SignalCount = sources.Length
        };

    private static TargetConsistencyRecord Record(
        string key, int inputs, int sources,
        LocationStatus location = LocationStatus.Aligned,
        TimelineStatus timeline = TimelineStatus.Coherent) => new()
        {
            TargetKey = key,
            InputCount = inputs,
            DistinctSourceCount = sources,
            Location = location,
            Timeline = timeline,
            Verdict = TargetConsistencyVerdict.Consistent
        };

    private static EvidenceConflict Conflict(string key, ConflictSeverity severity) => new()
    {
        TargetKey = key,
        Kind = ConflictKind.Location,
        Severity = severity
    };

    // Empty report
    [Fact]
    public void Assess_EmptyFused_ReturnsNoneOverall()
    {
        var report = new UncertaintyModelService().Assess(new EvidenceFusionReport(), null, null, null);

        report.TotalTargets.Should().Be(0);
        report.OverallLevel.Should().Be(UncertaintyLevel.None);
    }

    // Stage 1 — fully known target: zero uncertainty, Low
    [Fact]
    public void Assess_FullyKnownTarget_ZeroScoreLowLevel()
    {
        var fused = new EvidenceFusionReport
        {
            Items = new List<FusedEvidenceItem> { Item("T1", CandidateEvidenceType.Stack, CandidateEvidenceType.Coverage) }
        };
        var consistency = new EvidenceConsistencyReport
        {
            Targets = new List<TargetConsistencyRecord> { Record("T1", 2, 2) }
        };
        var confidence = new LocalizationConfidenceReport
        {
            Targets = new List<TargetConfidence>
            {
                new() { TargetKey = "T1", ConfidenceScore = 0.7, Level = ConfidenceLevel.High }
            }
        };

        var report = new UncertaintyModelService().Assess(fused, consistency, null, confidence);

        var t = report.Targets[0];
        t.KnownInputCount.Should().Be(2);
        t.KnownSourceCount.Should().Be(2);
        t.KnownSignalCount.Should().Be(2);
        t.KnownConfidence.Should().Be(0.7);
        t.MissingEvidence.Should().BeEmpty();
        t.UnknownAspects.Should().BeEmpty();
        t.UncertaintyScore.Should().Be(0.0);
        t.Level.Should().Be(UncertaintyLevel.Low);
    }

    // Stage 2 — single source: missing corroboration, Medium
    [Fact]
    public void Assess_SingleSource_MissingCorroborationMedium()
    {
        var fused = new EvidenceFusionReport
        {
            Items = new List<FusedEvidenceItem> { Item("T1", CandidateEvidenceType.Stack) }
        };
        var consistency = new EvidenceConsistencyReport
        {
            Targets = new List<TargetConsistencyRecord> { Record("T1", 1, 1) }
        };

        var report = new UncertaintyModelService().Assess(fused, consistency, null, null);

        var t = report.Targets[0];
        t.MissingCorroboration.Should().BeTrue();
        t.MissingEvidence.Should().Contain(m => m.Contains("only 1 distinct source"));
        t.UncertaintyScore.Should().BeApproximately(0.4, 0.001);  // 0.3 corroboration + 0.1 single signal
        t.Level.Should().Be(UncertaintyLevel.Medium);
    }

    // Stage 2 — missing location flagged explicitly
    [Fact]
    public void Assess_MissingLocation_ListedAndScored()
    {
        var fused = new EvidenceFusionReport
        {
            Items = new List<FusedEvidenceItem> { Item("T1", CandidateEvidenceType.Stack, CandidateEvidenceType.Coverage) }
        };
        var consistency = new EvidenceConsistencyReport
        {
            Targets = new List<TargetConsistencyRecord>
            {
                Record("T1", 2, 2, location: LocationStatus.Unknown)
            }
        };

        var report = new UncertaintyModelService().Assess(fused, consistency, null, null);

        var t = report.Targets[0];
        t.MissingLocation.Should().BeTrue();
        t.MissingEvidence.Should().Contain(m => m.StartsWith("Location:"));
        t.UncertaintyScore.Should().BeApproximately(0.2, 0.001);
        t.Level.Should().Be(UncertaintyLevel.Low);
    }

    // Stage 2 — missing timeline flagged explicitly
    [Fact]
    public void Assess_MissingTimeline_ListedAndScored()
    {
        var fused = new EvidenceFusionReport
        {
            Items = new List<FusedEvidenceItem> { Item("T1", CandidateEvidenceType.Stack, CandidateEvidenceType.Coverage) }
        };
        var consistency = new EvidenceConsistencyReport
        {
            Targets = new List<TargetConsistencyRecord>
            {
                Record("T1", 2, 2, timeline: TimelineStatus.Unknown)
            }
        };

        var report = new UncertaintyModelService().Assess(fused, consistency, null, null);

        var t = report.Targets[0];
        t.MissingTimeline.Should().BeTrue();
        t.MissingEvidence.Should().Contain(m => m.StartsWith("Timeline:"));
        t.UncertaintyScore.Should().BeApproximately(0.1, 0.001);
    }

    // Stage 3 — conflicts counted and penalized
    [Fact]
    public void Assess_Conflicts_CountedAndPenalized()
    {
        var fused = new EvidenceFusionReport
        {
            Items = new List<FusedEvidenceItem> { Item("T1", CandidateEvidenceType.Stack) }
        };
        var consistency = new EvidenceConsistencyReport
        {
            Targets = new List<TargetConsistencyRecord> { Record("T1", 1, 1) }
        };
        var conflicts = new ConflictAnalysisReport
        {
            Conflicts = new List<EvidenceConflict>
            {
                Conflict("T1", ConflictSeverity.High),
                Conflict("T1", ConflictSeverity.Low)
            }
        };

        var report = new UncertaintyModelService().Assess(fused, consistency, conflicts, null);

        var t = report.Targets[0];
        t.ConflictCount.Should().Be(2);
        t.HighSeverityConflicts.Should().Be(1);
        // 0.3 + 0.1 (single signal) + 0.2 (High) + 0.05 (Low) = 0.65
        t.UncertaintyScore.Should().BeApproximately(0.65, 0.001);
        t.Level.Should().Be(UncertaintyLevel.High);
    }

    // Stage 4 — package-only target (InputCount = 0)
    [Fact]
    public void Assess_PackageOnlyTarget_UnknownAspectRecorded()
    {
        var fused = new EvidenceFusionReport
        {
            Items = new List<FusedEvidenceItem> { Item("Ghost", CandidateEvidenceType.Regression, CandidateEvidenceType.Historical) }
        };
        var consistency = new EvidenceConsistencyReport
        {
            Targets = new List<TargetConsistencyRecord>
            {
                Record("Ghost", 0, 2, location: LocationStatus.Unknown, timeline: TimelineStatus.Unknown)
            }
        };
        consistency.Targets[0].Verdict = TargetConsistencyVerdict.Unknown;

        var report = new UncertaintyModelService().Assess(fused, consistency, null, null);

        var t = report.Targets[0];
        t.UnknownAspects.Should().Contain(a => a.Contains("package-only"));
        // 0.2 (location) + 0.1 (timeline) + 0.2 (unknown) = 0.5
        t.UncertaintyScore.Should().BeApproximately(0.5, 0.001);
        t.Level.Should().Be(UncertaintyLevel.Medium);
    }

    // Stage 4 — consistency not evaluated (null report)
    [Fact]
    public void Assess_NoConsistencyReport_UnknownAspectRecorded()
    {
        var fused = new EvidenceFusionReport
        {
            Items = new List<FusedEvidenceItem> { Item("T1", CandidateEvidenceType.Stack, CandidateEvidenceType.Coverage) }
        };

        var report = new UncertaintyModelService().Assess(fused, null, null, null);

        var t = report.Targets[0];
        t.UnknownAspects.Should().Contain(a => a.Contains("consistency not evaluated"));
        t.UncertaintyScore.Should().BeApproximately(0.2, 0.001);
    }

    // Stage 5 — overall level from average + ordering
    [Fact]
    public void Assess_OverallLevel_FromAverageScore()
    {
        var fused = new EvidenceFusionReport
        {
            Items = new List<FusedEvidenceItem>
            {
                Item("Good", CandidateEvidenceType.Stack, CandidateEvidenceType.Coverage),
                Item("Bad", CandidateEvidenceType.Stack)
            }
        };
        var consistency = new EvidenceConsistencyReport
        {
            Targets = new List<TargetConsistencyRecord>
            {
                Record("Good", 2, 2),
                Record("Bad", 1, 1)
            }
        };

        var report = new UncertaintyModelService().Assess(fused, consistency, null, null);

        report.TotalTargets.Should().Be(2);
        // Good = 0.0, Bad = 0.4 -> avg 0.2 -> Low; Bad ordered first
        report.Targets[0].TargetKey.Should().Be("Bad");
        report.AverageUncertainty.Should().BeApproximately(0.2, 0.001);
        report.OverallLevel.Should().Be(UncertaintyLevel.Low);
        report.HighUncertaintyCount.Should().Be(0);
    }
}