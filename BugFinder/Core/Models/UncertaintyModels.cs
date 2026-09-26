using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-14.7: Uncertainty Model
/// Declares what the investigation does NOT know - honestly and explicitly.
/// Known (14.1/14.6) + Missing (14.4 gaps) + Conflicting (14.5) + Unknown
/// states combine into one uncertainty score per target.
/// Principle: Missing Evidence != Failure; uncertainty is reported, never hidden.
/// </summary>

public enum UncertaintyLevel
{
    None,     // empty report
    Low,      // score < 0.3
    Medium,   // score >= 0.3
    High      // score >= 0.6
}

/// <summary>Uncertainty profile for one target.</summary>
public class TargetUncertainty
{
    public string TargetKey { get; set; } = string.Empty;

    // Stage 1 — Known
    public int KnownInputCount { get; set; }
    public int KnownSourceCount { get; set; }
    public int KnownSignalCount { get; set; }
    public double KnownConfidence { get; set; }          // from BF-14.6 (0 if absent)

    // Stage 2 — Missing (explicit, human-readable)
    public List<string> MissingEvidence { get; set; } = new();
    public bool MissingCorroboration { get; set; }
    public bool MissingLocation { get; set; }
    public bool MissingTimeline { get; set; }

    // Stage 3 — Conflicting
    public int ConflictCount { get; set; }
    public int HighSeverityConflicts { get; set; }

    // Stage 4 — Unknown state
    public List<string> UnknownAspects { get; set; } = new();

    // Stage 5
    public double UncertaintyScore { get; set; }         // clamped [0, 1]
    public UncertaintyLevel Level { get; set; }
}

/// <summary>Stage 5: report — feeds BF-14.8 Investigation Report.</summary>
public class InvestigationUncertaintyReport
{
    public List<TargetUncertainty> Targets { get; set; } = new();

    public int TotalTargets { get; set; }
    public int HighUncertaintyCount { get; set; }
    public double AverageUncertainty { get; set; }
    public UncertaintyLevel OverallLevel { get; set; }

    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;

    public const string Disclaimer =
        "Uncertainty is reported honestly: Missing Evidence != Failure, Unknown != Failure.";
}