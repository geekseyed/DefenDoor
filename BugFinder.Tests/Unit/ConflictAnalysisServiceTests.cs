using System;
using System.Collections.Generic;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class ConflictAnalysisServiceTests
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

    // Stage 1 — clean inputs produce zero conflicts
    [Fact]
    public void Analyze_CleanInputs_NoConflicts()
    {
        var inputs = new[]
        {
            Input(CandidateEvidenceType.Stack,    file: "a.cs", at: T0,             symbol: "M|R|OK()"),
            Input(CandidateEvidenceType.Coverage, file: "a.cs", at: T0.AddHours(1), symbol: "M|R|OK()")
        };
        var consistency = new EvidenceConsistencyService().Evaluate(inputs, null);

        var report = new ConflictAnalysisService().Analyze(inputs, consistency);

        report.Conflicts.Should().BeEmpty();
        report.TotalTargets.Should().Be(0);
    }

    // Stage 2+3 — location conflict: High severity + parties listed per path
    [Fact]
    public void Analyze_LocationConflict_HighSeverityWithParties()
    {
        var inputs = new[]
        {
            Input(CandidateEvidenceType.Stack,      file: "Calc.cs", symbol: "M|R|Split()"),
            Input(CandidateEvidenceType.Historical, file: "Other.cs", symbol: "M|R|Split()")
        };
        var consistency = new EvidenceConsistencyService().Evaluate(inputs, null);

        var report = new ConflictAnalysisService().Analyze(inputs, consistency);

        report.Conflicts.Should().HaveCount(1);
        var conflict = report.Conflicts[0];
        conflict.Kind.Should().Be(ConflictKind.Location);
        conflict.Severity.Should().Be(ConflictSeverity.High);
        conflict.Details.Should().Contain(d => d.StartsWith("Calc.cs"));
        conflict.Details.Should().Contain(d => d.StartsWith("Other.cs"));
        conflict.SourcesFor.Should().HaveCount(2);
        conflict.Resolution.Should().Be(ConflictResolution.Unresolved);   // honest default
        report.HighSeverityCount.Should().Be(1);
        report.UnresolvedCount.Should().Be(1);
    }

    // Stage 2+3 — timeline conflict severity scales with spread magnitude
    [Fact]
    public void Analyze_TimelineConflict_SeverityScales()
    {
        var inputs = new[]
        {
            Input(CandidateEvidenceType.Stack,    file: "a.cs", at: T0,               symbol: "M|R|Spread()"),
            Input(CandidateEvidenceType.Coverage, file: "a.cs", at: T0.AddHours(30), symbol: "M|R|Spread()")   // >24h, <=48h -> Low
        };
        var consistency = new EvidenceConsistencyService().Evaluate(inputs, null);

        var report = new ConflictAnalysisService().Analyze(inputs, consistency);

        report.Conflicts.Should().HaveCount(1);
        report.Conflicts[0].Kind.Should().Be(ConflictKind.Timeline);
        report.Conflicts[0].Severity.Should().Be(ConflictSeverity.Low);

        // far beyond tolerance -> Medium
        var farInputs = new[]
        {
            Input(CandidateEvidenceType.Stack,    file: "b.cs", at: T0,                symbol: "M|R|Far()"),
            Input(CandidateEvidenceType.Coverage, file: "b.cs", at: T0.AddHours(120), symbol: "M|R|Far()")
        };
        var farConsistency = new EvidenceConsistencyService().Evaluate(farInputs, null);

        var farReport = new ConflictAnalysisService().Analyze(farInputs, farConsistency);

        farReport.Conflicts[0].Severity.Should().Be(ConflictSeverity.Medium);
    }

    // No timestamps -> no timeline conflict (Unknown is not a conflict)
    [Fact]
    public void Analyze_NoTimestamps_NoTimelineConflict()
    {
        var inputs = new[]
        {
            Input(CandidateEvidenceType.Stack,    file: "a.cs", symbol: "M|R|NoTime()"),
            Input(CandidateEvidenceType.Coverage, file: "a.cs", symbol: "M|R|NoTime()")
        };
        var consistency = new EvidenceConsistencyService().Evaluate(inputs, null);

        var report = new ConflictAnalysisService().Analyze(inputs, consistency);

        report.Conflicts.Should().BeEmpty();
    }

    // Stage 2 — source-count mismatch between package and raw (Medium)
    [Fact]
    public void Analyze_SourceCountMismatch_MediumSeverity()
    {
        var inputs = new[] { Input(CandidateEvidenceType.Stack, file: "a.cs", symbol: "M|R|Ghost()") };
        var consistency = new EvidenceConsistencyReport
        {
            Targets = new List<TargetConsistencyRecord>
            {
                new() { TargetKey = "M|R|Ghost()", InputCount = 1, DistinctSourceCount = 2 }
            }
        };

        var report = new ConflictAnalysisService().Analyze(inputs, consistency);

        report.Conflicts.Should().HaveCount(1);
        report.Conflicts[0].Kind.Should().Be(ConflictKind.SourceCount);
        report.Conflicts[0].Severity.Should().Be(ConflictSeverity.Medium);
        report.Conflicts[0].Details[0].Should().Contain("package claims 2");
    }

    // Stage 4 — multiple conflicts on one target all preserved unresolved
    [Fact]
    public void Analyze_MultipleConflicts_AllPreservedUnresolved()
    {
        var inputs = new[]
        {
            Input(CandidateEvidenceType.Stack,    file: "X.cs", at: T0,                symbol: "M|R|Messy()"),
            Input(CandidateEvidenceType.Coverage, file: "Y.cs", at: T0.AddHours(120), symbol: "M|R|Messy()")
        };
        var consistency = new EvidenceConsistencyService().Evaluate(inputs, null);

        var report = new ConflictAnalysisService().Analyze(inputs, consistency);

        report.TotalTargets.Should().Be(1);
        report.Conflicts.Should().HaveCount(2);   // Location + Timeline
        report.UnresolvedCount.Should().Be(2);
        report.Conflicts.Should().OnlyContain(c => c.Resolution == ConflictResolution.Unresolved);
    }

    // Fused-only target (no raw inputs) cannot produce conflicts (no parties)
    [Fact]
    public void Analyze_FusedOnlyTarget_NoConflicts()
    {
        var consistency = new EvidenceConsistencyReport
        {
            Targets = new List<TargetConsistencyRecord>
            {
                new() { TargetKey = "M|R|Ghost()", InputCount = 0, DistinctSourceCount = 2 }
            }
        };
        
        var report = new ConflictAnalysisService().Analyze(null, consistency);

        report.Conflicts.Should().BeEmpty();
    }
}