using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-15.4: Dynamic Slicing Models (research only).
/// Computes the failure-relevant backward slice over a caller-supplied
/// execution trace. The Core never instruments or executes code
/// (Strict Core Boundary, BF-14.9) - the trace is a contract.
/// </summary>

/// <summary>One recorded operation of the execution trace (Stage 2 model).</summary>
public class TraceOperation
{
    public string OperationId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int? LineNumber { get; set; }
    public string Description { get; set; } = string.Empty;

    /// <summary>Variable written by this operation (def), if any.</summary>
    public string? WrittenVariable { get; set; }

    /// <summary>Variables read by this operation (uses).</summary>
    public List<string> ReadVariables { get; set; } = new();

    /// <summary>Branch-condition variable this operation executed under, if any (control dep as data).</summary>
    public string? ControlVariable { get; set; }
}

/// <summary>Stage 1: the wrong variable and where the failure was observed.</summary>
public class FailureVariableInput
{
    public string VariableName { get; set; } = string.Empty;
    public string AtOperationId { get; set; } = string.Empty;
}

public enum DynamicSliceStatus
{
    Sliced,                      // backward slice computed
    UnresolvedFailureOperation,  // AtOperationId not present in the trace (honest)
    Empty                        // no trace operations at all
}

/// <summary>Stage 4: failure-relevant slice report.</summary>
public class DynamicSliceReport
{
    public string ScenarioName { get; set; } = string.Empty;
    public DynamicSliceStatus Status { get; set; }

    public int OriginalOperationCount { get; set; }
    public int SliceOperationCount { get; set; }
    public double SliceRatio { get; set; }

    /// <summary>Slice in execution (trace) order.</summary>
    public List<string> SliceOperationIds { get; set; } = new();
    public List<TraceOperation> SliceOperations { get; set; } = new();
    public List<string> ExcludedOperationIds { get; set; } = new();

    /// <summary>Single backward pass is a fixpoint for reaching-defs slicing.</summary>
    public bool IsBackwardClosed { get; set; }

    public bool IsExperimental { get; set; } = true;
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;

    public const string DisclaimerText =
        "EXPERIMENTAL (BF-15.4): dynamic-slicing research evidence - not production guidance. " +
        "Computed from a caller-supplied trace; the Core never instruments or executes code.";

    /// <summary>Serializable view (const strings are invisible to System.Text.Json - 15.7 lesson).</summary>
    public string Disclaimer => DisclaimerText;
}