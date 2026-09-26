using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-14.6: Localization Confidence Models
/// Converts evidence support (BF-14.1), signal agreement (BF-14.2 mapping)
/// and conflict penalties (BF-14.5) into one calibrated confidence per target.
/// Principle: Confidence != Root Cause - it ranks investigation priority only.
/// </summary>

public enum ConfidenceLevel
{
    None,       // no evidence at all (zero signals)
    VeryLow,    // score < 0.2 (usually heavy conflict penalties)
    Low,        // score >= 0.2
    Medium,     // score >= 0.4
    High,       // score >= 0.6
    VeryHigh    // score >= 0.8
}

/// <summary>Confidence record for one target.</summary>
public class TargetConfidence
{
    public string TargetKey { get; set; } = string.Empty;

    // Stage 1 — support inputs
    public double FusedStrength { get; set; }
    public int SupportingSignalCount { get; set; }
    public int InputCount { get; set; }

    // Stage 2-3 — modifiers
    public double AgreementBoost { get; set; }       // e.g. 1.0 / 1.1 / 1.2 / 1.3
    public double ConflictPenalty { get; set; }      // summed penalty (>= 0)
    public int HighConflicts { get; set; }
    public int MediumConflicts { get; set; }
    public int LowConflicts { get; set; }

    // Stage 4-5
    public double ConfidenceScore { get; set; }      // clamped [0, 1]
    public ConfidenceLevel Level { get; set; }
}

/// <summary>Stage 5: report — input for BF-14.8 Investigation Report.</summary>
public class LocalizationConfidenceReport
{
    public List<TargetConfidence> Targets { get; set; } = new();

    public int TotalTargets { get; set; }
    public int HighConfidenceCount { get; set; }     // High + VeryHigh
    public int LowConfidenceCount { get; set; }      // None + VeryLow + Low
    public TargetConfidence? TopTarget { get; set; }

    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;

    public const string Disclaimer = "Confidence != Root Cause. Scores rank investigation priority only.";
}