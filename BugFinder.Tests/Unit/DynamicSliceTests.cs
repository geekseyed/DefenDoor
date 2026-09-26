using System;
using System.Collections.Generic;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class DynamicSliceTests
{
    private readonly DynamicSliceService _service = new();

    private static TraceOperation Op(
    string id, string? writes = null, string[]? reads = null, string? control = null) => new()
    {
        OperationId = id,
        FilePath = "Calc.cs",
        LineNumber = 10,
        Description = $"op {id}",
        WrittenVariable = writes,
        ReadVariables = reads?.ToList() ?? new List<string>(),
        ControlVariable = control
    };

    // Stages 3-4 - full hand-traced slice (data + control chain), junk excluded
    [Fact]
    public void Slice_BackwardChain_ExcludesIrrelevantOperation()
    {
        // Trace (derivable from the reaching-def algorithm):
        //   S1 writes x;  S2 writes junk (IRRELEVANT);  S3 writes flag reads[x];
        //   S4 writes y reads[x];  S5 writes z reads[y];  S6 writes w reads[z] control[flag];
        //   S7 reads[w]  <- failure point, failure var = w
        var trace = new[]
        {
            Op("S1", writes: "x"),
            Op("S2", writes: "junk"),
            Op("S3", writes: "flag", reads: new[] { "x" }),
            Op("S4", writes: "y",    reads: new[] { "x" }),
            Op("S5", writes: "z",    reads: new[] { "y" }),
            Op("S6", writes: "w",    reads: new[] { "z" }, control: "flag"),
            Op("S7", reads: new[] { "w" })
        };

        var report = _service.Slice("Chain", trace,
            new FailureVariableInput { VariableName = "w", AtOperationId = "S7" });

        report.Status.Should().Be(DynamicSliceStatus.Sliced);
        report.SliceOperationIds.Should().ContainInOrder("S1", "S3", "S4", "S5", "S6", "S7");
        report.ExcludedOperationIds.Should().ContainSingle().Which.Should().Be("S2");
        report.SliceOperationCount.Should().Be(6);
        report.OriginalOperationCount.Should().Be(7);
        report.SliceRatio.Should().BeApproximately(1.0 / 7.0, 0.001);   // 1 - (2/3): 2 of 3 ops kept
        report.IsBackwardClosed.Should().BeTrue();

        // control dependence resolved as data: flag's writer (S3) is inside the slice
        report.SliceOperations.Should().Contain(op => op.OperationId == "S3");
    }

    // Stage 4 - killed defs: only the reaching def of the failure variable is included
    [Fact]
    public void Slice_ReachingDefinitionOnly_KilledOlderWriteExcluded()
    {
        var trace = new[]
        {
            Op("S1", writes: "x"),   // killed by S2 - excluded
            Op("S2", writes: "x"),   // reaching def - included
            Op("S3", reads: new[] { "x" })   // failure point
        };

        var report = _service.Slice("KilledDef", trace,
            new FailureVariableInput { VariableName = "x", AtOperationId = "S3" });

        report.SliceOperationIds.Should().ContainInOrder("S2", "S3");
        report.ExcludedOperationIds.Should().ContainSingle().Which.Should().Be("S1");
        report.SliceRatio.Should().BeApproximately(1.0 / 3.0, 0.001);
    }

    // Stage 1 - failure variable never written in the trace (external input):
    // slice = observation point only; unmatched need is honest, no fabrication
    [Fact]
    public void Slice_ExternallySourcedVariable_SliceIsObservationOnly()
    {
        var trace = new[]
        {
            Op("S1", writes: "junk"),
            Op("S2", reads: new[] { "x" })   // failure point; x never written here
        };

        var report = _service.Slice("External", trace,
            new FailureVariableInput { VariableName = "x", AtOperationId = "S2" });

        report.Status.Should().Be(DynamicSliceStatus.Sliced);
        report.SliceOperationIds.Should().ContainSingle().Which.Should().Be("S2");
        report.ExcludedOperationIds.Should().ContainSingle().Which.Should().Be("S1");
    }

    // Stage 1 - failure point writes the variable itself: wrongness is in its inputs
    [Fact]
    public void Slice_FailurePointWritesVariable_InputsTrackedInstead()
    {
        var trace = new[]
        {
            Op("S1", writes: "a"),
            Op("S2", writes: "x", reads: new[] { "a" }),   // failure point, writes x itself
            Op("S3", writes: "x")                           // older def - killed
        };

        var report = _service.Slice("SelfWrite", trace,
            new FailureVariableInput { VariableName = "x", AtOperationId = "S2" });

        report.SliceOperationIds.Should().ContainInOrder("S1", "S2");
        report.ExcludedOperationIds.Should().ContainSingle().Which.Should().Be("S3");
    }

    // Stage 1 - honest unresolved status
    [Fact]
    public void Slice_UnknownFailureOperation_HonestUnresolvedStatus()
    {
        var trace = new[] { Op("S1", writes: "x") };

        var report = _service.Slice("Ghost", trace,
            new FailureVariableInput { VariableName = "x", AtOperationId = "NOPE" });

        report.Status.Should().Be(DynamicSliceStatus.UnresolvedFailureOperation);
        report.SliceOperationIds.Should().BeEmpty();
        report.SliceOperationCount.Should().Be(0);
        report.SliceRatio.Should().Be(0.0);
        report.IsBackwardClosed.Should().BeFalse();
    }

    // Stage 1 - empty trace
    [Fact]
    public void Slice_EmptyTrace_EmptyStatus()
    {
        var report = _service.Slice("Empty",
            Array.Empty<TraceOperation>(),
            new FailureVariableInput { VariableName = "x", AtOperationId = "S1" });

        report.Status.Should().Be(DynamicSliceStatus.Empty);
        report.OriginalOperationCount.Should().Be(0);
    }

    // Contract violations fail fast
    [Fact]
    public void Slice_NullOrInvalidArguments_Throw()
    {
        var trace = new[] { Op("S1") };
        var failure = new FailureVariableInput { VariableName = "x", AtOperationId = "S1" };

        Action nullTrace = () => _service.Slice("S", null!, failure);
        Action nullFailure = () => _service.Slice("S", trace, null!);
        Action emptyVariable = () => _service.Slice("S", trace,
            new FailureVariableInput { VariableName = " ", AtOperationId = "S1" });
        Action emptyOperation = () => _service.Slice("S", trace,
            new FailureVariableInput { VariableName = "x", AtOperationId = " " });

        nullTrace.Should().Throw<ArgumentNullException>();
        nullFailure.Should().Throw<ArgumentNullException>();
        emptyVariable.Should().Throw<ArgumentException>();
        emptyOperation.Should().Throw<ArgumentException>();
    }

    // Research contract markers (serializable disclaimer - 15.7 lesson)
    [Fact]
    public void Report_CarriesResearchMarkers()
    {
        DynamicSliceReport.DisclaimerText.Should().Contain("EXPERIMENTAL");
        DynamicSliceReport.DisclaimerText.Should().Contain("caller-supplied");
    }
}