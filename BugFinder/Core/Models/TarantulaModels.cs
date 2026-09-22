using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-12.5: Tarantula Algorithm Models
/// </summary>

/// <summary>
/// Configuration specific to Tarantula heuristic tuning.
/// </summary>
public class TarantulaConfig
{
    /// <summary>
    /// Threshold for flagging a location as "Highly Suspicious".
    /// Default is 0.8 (80% likelihood based on ratio).
    /// </summary>
    public double HighSuspicionThreshold { get; set; } = 0.8;

    /// <summary>
    /// If true, normalizes scores to 0.0-1.0 range even if raw ratios exceed 1.0 (rare).
    /// </summary>
    public bool NormalizeScores { get; set; } = true;
}

/// <summary>
/// Result container for a single Tarantula analysis run.
/// </summary>
public class TarantulaResult
{
    public List<SuspiciousLocation> RankedLocations { get; set; } = new();
    public int TotalElementsAnalyzed { get; set; }
    public double MaxScoreAchieved { get; set; }
    public string WarningMessage { get; set; } = string.Empty;
}