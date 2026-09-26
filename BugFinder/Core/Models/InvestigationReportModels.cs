using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-14.8: Investigation Report Models
/// The official terminal output of the Bug Finder Core. Assembles all
/// BF-14.1..14.7 sub-reports into one self-contained report with the
/// investigation timeline. CORE STOPS HERE - no repair beyond this point.
/// </summary>

public enum InvestigationOutcome
{
    Complete,               // full chain completed (BF-14.3 IsComplete)
    Partial,                // investigation ran but gaps remain
    InsufficientEvidence    // no failure identity and no evidence provided
}

/// <summary>Stage 4: compact evidence glance (full reports held separately).</summary>
public class EvidenceSummary
{
    public int TotalInputs { get; set; }
    public int TotalTargets { get; set; }
    public int FullyCorroboratedCount { get; set; }
    public ReportConsistencyStatus? ConsistencyStatus { get; set; }
    public int ConflictCount { get; set; }
    public int UnresolvedConflictCount { get; set; }
}

/// <summary>Input container for Assemble() - pre-computed sub-reports.</summary>
public class InvestigationReportInput
{
    public FailureIdentityInput? Failure { get; set; }
    public IReadOnlyList<FailureLocation>? Locations { get; set; }

    public EvidenceFusionReport? FusedEvidence { get; set; }
    public MultiSignalCorrelationReport? Correlation { get; set; }
    public InvestigationChainReport? Chain { get; set; }
    public EvidenceConsistencyReport? Consistency { get; set; }
    public ConflictAnalysisReport? Conflicts { get; set; }
    public LocalizationConfidenceReport? Confidence { get; set; }
    public InvestigationUncertaintyReport? Uncertainty { get; set; }

    /// <summary>Used only for the correlation fallback when Chain is absent.</summary>
    public int MaxSuspiciousLocations { get; set; } = 5;
}

/// <summary>The official report — terminal output of the Core.</summary>
public class InvestigationReport
{
    // Stage 1-2: Failure Summary + Classification
    public FailureIdentityInput? Failure { get; set; }

    // Stage 3: Failure Locations
    public List<FailureLocation> Locations { get; set; } = new();

    // Stage 4: Evidence Summary (compact)
    public EvidenceSummary? Evidence { get; set; }

    // Stage 5: Suspicious Locations (top-N, order preserved)
    public List<CorrelatedTarget> SuspiciousLocations { get; set; } = new();

    // Stage 6: Confidence (full per-target report; join via TargetKey)
    public LocalizationConfidenceReport? Confidence { get; set; }

    // Stage 7: Uncertainty
    public InvestigationUncertaintyReport? Uncertainty { get; set; }

    // Stage 8: Investigation Timeline (canonical chain steps)
    public List<InvestigationStep> Timeline { get; set; } = new();

    public InvestigationOutcome Outcome { get; set; }
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;

    // ---- Backing sub-reports (self-contained drill-down) ----
    public EvidenceFusionReport? FusedEvidence { get; set; }
    public MultiSignalCorrelationReport? Correlation { get; set; }
    public InvestigationChainReport? Chain { get; set; }
    public EvidenceConsistencyReport? Consistency { get; set; }
    public ConflictAnalysisReport? Conflicts { get; set; }

    // ---- Contract constants ----
    public const string DisclaimerSuspiciousness = "Suspiciousness != Root Cause (BF-12).";
    public const string DisclaimerConfidence = "Confidence != Root Cause (BF-14.6).";
    public const string DisclaimerUncertainty = "Missing Evidence != Failure; Unknown != Failure (BF-14.7).";

    /// <summary>The architectural stop marker: Core is read-only, ends here.</summary>
    public const string CoreBoundary =
        "CORE STOPS HERE: this report is the terminal output of the Bug Finder Core. " +
        "No patch, no fix, no auto-repair is produced by the Core. " +
        "Optional remediation belongs to a separate extension outside BF-00..BF-15.";
}