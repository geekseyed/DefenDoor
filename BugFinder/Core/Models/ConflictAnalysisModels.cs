using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-14.5: Conflicting Evidence Models
/// Deep analysis of conflicts detected by BF-14.4. Detection vs analysis:
/// 14.4 measures consistency, 14.5 classifies and preserves conflicts.
/// Principle: unresolved conflicts are preserved, never fabricated away.
/// </summary>

/// <summary>What kind of disagreement is this?</summary>
public enum ConflictKind
{
    Location,     // sources point to different file paths
    Timeline,     // observations spread beyond tolerance
    SourceCount   // fused package claims corroboration the raw inputs lack (or vice versa)
}

/// <summary>How was this conflict resolved? (Default: Unresolved — honest)</summary>
public enum ConflictResolution
{
    Unresolved,   // preserved as-is; investigation continues with known conflict
    Explained,    // e.g., same-name file in different folders (future: path normalization context)
    Dismissed     // future: with hard justification (e.g., stale artifact)
}

/// <summary>Severity of a single conflict.</summary>
public enum ConflictSeverity
{
    Low,          // informational (e.g., mild timeline spread)
    Medium,       // weakens confidence (e.g., source-count mismatch)
    High          // contradicts localization (location divergence)
}

/// <summary>Stage 1-3: one classified conflict with its parties.</summary>
public class EvidenceConflict
{
    public ConflictKind Kind { get; set; }
    public ConflictSeverity Severity { get; set; }
    public string TargetKey { get; set; } = string.Empty;

    // Stage 3 — the parties: which sources are on which side
    public List<CandidateEvidenceType> SourcesFor { get; set; } = new();
    public List<string> Details { get; set; } = new();   // human-readable facts (paths / spans / counts)

    public ConflictResolution Resolution { get; set; } = ConflictResolution.Unresolved;
}

/// <summary>Stage 4: conflict analysis report — input for BF-14.6 Confidence.</summary>
public class ConflictAnalysisReport
{
    public List<EvidenceConflict> Conflicts { get; set; } = new();

    public int TotalTargets { get; set; }
    public int ConflictFreeTargets { get; set; }
    public int ConflictedTargets { get; set; }

    public int HighSeverityCount { get; set; }
    public int MediumSeverityCount { get; set; }
    public int LowSeverityCount { get; set; }

    public int UnresolvedCount { get; set; }

    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}