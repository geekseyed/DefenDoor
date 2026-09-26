using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class DeltaDebuggingTests
{
    private readonly DeltaDebuggingService _service = new();

    private static bool Contains(IReadOnlyList<string> chunks, string token) =>
        chunks.Any(c => c.Contains(token, StringComparison.Ordinal));

    // Stages 2-4 — greedy elimination to a 1-minimal core, hand-traced
    [Fact]
    public void Reduce_IrrelevantChunksRemovedToOneMinimal_HandTraced()
    {
        var chunks = new[] { "S1", "FAULT", "S2", "N1" };

        var report = _service.Reduce("Trace", chunks, c => Contains(c, "FAULT"));

        report.Status.Should().Be(DeltaDebuggingStatus.Reduced);
        report.RemainingChunks.Should().ContainSingle().Which.Should().Be("FAULT");
        report.MinimalChunkCount.Should().Be(1);
        report.OriginalChunkCount.Should().Be(4);
        report.ReductionRatio.Should().BeApproximately(0.75, 0.001);
        report.IsOneMinimal.Should().BeTrue();

        // Pass 1 (backward): N1 removed, S2 removed, FAULT kept, S1 removed
        // Pass 2 (verification): FAULT kept -> no progress -> stop
        report.PassCount.Should().Be(2);
        report.Attempts.Should().HaveCount(5);

        var pass1 = report.Attempts.Where(a => a.PassNumber == 1).ToList();
        pass1.Select(a => a.Chunk).Should().ContainInOrder("N1", "S2", "FAULT", "S1");
        pass1.Select(a => a.Removed).Should().ContainInOrder(true, true, false, true);
        pass1[2].ChunkIndex.Should().Be(1);

        report.RemovedChunks.Should().ContainInOrder("N1", "S2", "S1");
    }

    // Stage 3 — dependent chunks survive together (multi-pass fixpoint)
    [Fact]
    public void Reduce_DependentPairSurvives_MultiPassFixpoint()
    {
        var chunks = new[] { "A", "B", "NOISE" };

        var report = _service.Reduce("Dependent", chunks,
            c => Contains(c, "A") && Contains(c, "B"));

        report.Status.Should().Be(DeltaDebuggingStatus.Reduced);
        report.RemainingChunks.Should().HaveCount(2);
        report.RemainingChunks.Should().Contain("A").And.Contain("B");
        report.RemovedChunks.Should().ContainSingle().Which.Should().Be("NOISE");
        report.ReductionRatio.Should().BeApproximately(1.0 / 3.0, 0.001);
        report.IsOneMinimal.Should().BeTrue();   // removing A or B breaks the failure

        // Pass 1: NOISE removed, A kept, B kept; Pass 2: both kept -> stop
        report.PassCount.Should().Be(2);
        report.Attempts.Where(a => a.PassNumber == 2)
            .Should().OnlyContain(a => !a.Removed);
    }

    // Stage 1 — honest NotFailing: nothing reduced, nothing fabricated
    [Fact]
    public void Reduce_NotFailingInput_HonestStatusNoReduction()
    {
        var chunks = new[] { "A", "B" };

        var report = _service.Reduce("Green", chunks, _ => false);

        report.Status.Should().Be(DeltaDebuggingStatus.NotFailing);
        report.RemainingChunks.Should().ContainInOrder("A", "B");
        report.RemovedChunks.Should().BeEmpty();
        report.Attempts.Should().BeEmpty();
        report.PassCount.Should().Be(0);
        report.IsOneMinimal.Should().BeFalse();
        report.ReductionRatio.Should().Be(0.0);
    }

    // Stage 1 — empty input
    [Fact]
    public void Reduce_EmptyInput_EmptyStatus()
    {
        var report = _service.Reduce("Empty", Array.Empty<string>(), _ => true);

        report.Status.Should().Be(DeltaDebuggingStatus.Empty);
        report.MinimalChunkCount.Should().Be(0);
    }

    // Stage 4 — already minimal: single verification pass, zero removals
    [Fact]
    public void Reduce_AlreadyMinimal_SingleVerificationPass()
    {
        var report = _service.Reduce("Single", new[] { "FAULT" }, c => Contains(c, "FAULT"));

        report.Status.Should().Be(DeltaDebuggingStatus.Reduced);
        report.RemainingChunks.Should().ContainSingle().Which.Should().Be("FAULT");
        report.PassCount.Should().Be(1);
        report.Attempts.Should().ContainSingle().Which.Removed.Should().BeFalse();
        report.ReductionRatio.Should().Be(0.0);
        report.IsOneMinimal.Should().BeTrue();
    }

    // Order-sensitive predicate: both chunks survive the fixpoint
    [Fact]
    public void Reduce_OrderSensitivePredicate_PreservesBoth()
    {
        var chunks = new[] { "X", "OPEN", "CLOSE" };

        var report = _service.Reduce("Ordered", chunks,
            c => c.Count(s => s == "OPEN") == 1
                 && c.Count(s => s == "CLOSE") == 1
                 && c.ToList().IndexOf("OPEN") < c.ToList().IndexOf("CLOSE"));

        report.Status.Should().Be(DeltaDebuggingStatus.Reduced);
        report.RemainingChunks.Should().ContainInOrder("OPEN", "CLOSE");
        report.IsOneMinimal.Should().BeTrue();
    }

    // Contract violations fail fast
    [Fact]
    public void Reduce_NullArguments_Throw()
    {
        Action nullChunks = () => _service.Reduce("S", null!, _ => true);
        Action nullPredicate = () => _service.Reduce("S", new[] { "A" }, null!);
        Action nullName = () => _service.Reduce(null!, new[] { "A" }, _ => true);

        nullChunks.Should().Throw<ArgumentNullException>();
        nullPredicate.Should().Throw<ArgumentNullException>();
        nullName.Should().Throw<ArgumentNullException>();
    }

    // Research contract markers
    [Fact]
    public void Report_CarriesResearchMarkers()
    {
        DeltaDebuggingReport.DisclaimerText.Should().Contain("EXPERIMENTAL");
        DeltaDebuggingReport.DisclaimerText.Should().Contain("caller-supplied contract");
    }
}