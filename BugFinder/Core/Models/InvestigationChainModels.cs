using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-14.3: Investigation Chain Models
/// Assembles Failure -> Classification -> Localization -> Evidence ->
/// Correlation -> Suspicious Locations into one investigation chain.
/// Assembly layer only: consumes BF-14.1 + BF-14.2 outputs.
/// </summary>

/// <summary>Canonical chain order (enum order = pipeline order).</summary>
public enum InvestigationStageKind
{
    Failure, Classification, Localization, Evidence, Correlation, SuspiciousLocations, Chain
}

public enum InvestigationStageStatus
{
    Complete, Partial, Empty
}

/// <summary>One link of the chain with its status + human summary.</summary>
public class InvestigationStep
{
    public InvestigationStageKind Kind { get; set; }
    public InvestigationStageStatus Status { get; set; }
    public string Summary { get; set; } = string.Empty;
}

/// <summary>Stage 1-2 input: failure identity from BF-01/02 vocabulary.</summary>
public class FailureIdentityInput
{
    public string TestIdentity { get; set; } = string.Empty;      // e.g. NS.Class.Method_Test
    public string? EvaluationIdentity { get; set; }               // e.g. EVL-001.4 (domain)
    public string? FailureCategory { get; set; }                  // BF-02 classification
    public string? FailureSignature { get; set; }                 // BF-01.6 stable signature
}

/// <summary>Stage 3 input: one localized location (BF-03 vocabulary).</summary>
public class FailureLocation
{
    public string FilePath { get; set; } = string.Empty;
    public int? LineNumber { get; set; }
    public string? MethodName { get; set; }
    public bool IsPrimary { get; set; }
}

/// <summary>Stage 6: one suspicious location (NOT a root cause — see disclaimer).</summary>


/// <summary>Stage 7: the assembled investigation chain — input for BF-14.4.</summary>
public class InvestigationChainReport
{
    public FailureIdentityInput? Failure { get; set; }
    public List<InvestigationStep> Steps { get; set; } = new();
    public List<CorrelatedTarget> SuspiciousLocations { get; set; } = new();
    public bool IsComplete { get; set; }
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>BF-12 non-negotiable principle, carried into every report.</summary>
    public const string Disclaimer =
        "Suspiciousness != Root Cause. The chain is evidence support, never proof.";
}