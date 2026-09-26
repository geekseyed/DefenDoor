using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-14.4: Evidence Consistency Models
/// Measures whether evidence for the same target agrees across three
/// dimensions (source / location / timeline). Detection only — conflict
/// analysis belongs to BF-14.5.
/// </summary>

public enum CrossSourceStatus { Corroborated, SingleSource, Unknown }
public enum LocationStatus { Aligned, Divergent, Unknown }
public enum TimelineStatus { Coherent, Dispersed, Unknown }
public enum TargetConsistencyVerdict { Consistent, Partial, Inconsistent, Unknown }
public enum ReportConsistencyStatus { None, Consistent, Partial, Inconsistent }

/// <summary>Per-target consistency record across all three dimensions.</summary>
public class TargetConsistencyRecord
{
    public string TargetKey { get; set; } = string.Empty;
    public double FusedStrength { get; set; }
    public int InputCount { get; set; }
    public int DistinctSourceCount { get; set; }

    // Stage 1
    public CrossSourceStatus CrossSource { get; set; }

    // Stage 2
    public LocationStatus Location { get; set; }
    public List<string> DistinctLocations { get; set; } = new();

    // Stage 3
    public TimelineStatus Timeline { get; set; }
    public TimeSpan? TimelineSpread { get; set; }

    // Stage 4
    public TargetConsistencyVerdict Verdict { get; set; }
}

/// <summary>Stage 4: consistency result report — input for BF-14.5.</summary>
public class EvidenceConsistencyReport
{
    public List<TargetConsistencyRecord> Targets { get; set; } = new();

    public int TotalTargets { get; set; }
    public int ConsistentCount { get; set; }
    public int PartialCount { get; set; }
    public int InconsistentCount { get; set; }
    public int UnknownCount { get; set; }

    public ReportConsistencyStatus OverallStatus { get; set; }
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}