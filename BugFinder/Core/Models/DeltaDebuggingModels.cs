using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-15.3: Delta Debugging Models (research only).
/// Reduces a failing INPUT (an ordered list of chunks) to a minimal
/// failure-preserving core. The failure predicate is a caller-supplied
/// contract - the Core executes no tests itself (Strict Core Boundary).
/// </summary>

public enum DeltaDebuggingStatus
{
    Reduced,      // failure preserved and reduced to a 1-minimal core
    NotFailing,   // original input does not fail - nothing to reduce (honest)
    Empty         // no chunks at all
}

/// <summary>Stage 2/3 trace: one removal attempt.</summary>
public class ReductionAttempt
{
    public int PassNumber { get; set; }
    public int ChunkIndex { get; set; }
    public string Chunk { get; set; } = string.Empty;
    public bool Removed { get; set; }   // true = failure still preserved without it
}

/// <summary>Stage 4: minimal failure input report.</summary>
public class DeltaDebuggingReport
{
    public string ScenarioName { get; set; } = string.Empty;
    public DeltaDebuggingStatus Status { get; set; }

    public int OriginalChunkCount { get; set; }
    public int MinimalChunkCount { get; set; }
    public double ReductionRatio { get; set; }

    public List<string> RemovedChunks { get; set; } = new();
    public List<string> RemainingChunks { get; set; } = new();
    public List<ReductionAttempt> Attempts { get; set; } = new();

    /// <summary>1-minimal: removing ANY single remaining chunk breaks the failure.</summary>
    public bool IsOneMinimal { get; set; }
    public int PassCount { get; set; }

    public bool IsExperimental { get; set; } = true;
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;

    public const string DisclaimerText =
        "EXPERIMENTAL (BF-15.3): delta-debugging research evidence - not production guidance. " +
        "The Core never executes tests itself; the failure predicate is a caller-supplied contract.";

    /// <summary>Serializable view (const strings are invisible to System.Text.Json - 15.7 lesson).</summary>
    public string Disclaimer => DisclaimerText;
}