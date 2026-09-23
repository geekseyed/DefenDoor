using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-13.8: Static + Dynamic Correlation Models
/// Bridges dynamic evidence (stack / coverage / runtime / ranked candidates)
/// to static code structure (BF-13.4 StableKeys). The BF-13 Gate output.
/// </summary>

/// <summary>Where the dynamic evidence came from.</summary>
public enum DynamicEvidenceSource
{
    Stack,           // BF-03 stack frames
    Coverage,        // BF-06 executed lines
    Regression,      // BF-10 changed/hit locations
    Historical,      // BF-11 historical failure locations
    RankedCandidate, // BF-12.10 ranked fault candidates
    Runtime,         // BF-08 runtime events
    Manual           // ad-hoc user input
}

/// <summary>How a dynamic item was matched to static structure.</summary>
public enum CorrelationMatchStrategy
{
    None = 0,
    FileOnly = 1,
    MethodNameOnly = 2,
    LineWithinMethod = 3,
    MethodNameAndFile = 4
}

/// <summary>Normalized dynamic input (adapter target for all BF phases).</summary>
public class DynamicLocationEvidence
{
    public DynamicEvidenceSource SourceType { get; set; } = DynamicEvidenceSource.Manual;
    public string? SourceArtifact { get; set; }     // TRX path / coverage file / ElementId / SHA
    public string? FilePath { get; set; }
    public string? MethodName { get; set; }         // e.g. "Rel.ChildService.Compute" or "Compute"
    public int? LineNumber { get; set; }            // 1-based
    public double SignalStrength { get; set; }      // 0.0 - 1.0
    public string? Description { get; set; }
}

/// <summary>One static anchor: a declared method with its stable identity + span.</summary>
public class StaticMethodIndexEntry
{
    public string SymbolKey { get; set; } = string.Empty;    // BF-13.4 StableKey
    public string DisplayName { get; set; } = string.Empty;
    public string MethodName { get; set; } = string.Empty;
    public string ContainingTypeName { get; set; } = string.Empty;
    public string? ContainingNamespace { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public int StartLine { get; set; }              // 1-based, inclusive
    public int EndLine { get; set; }                // 1-based, inclusive
    public bool IsStatic { get; set; }
    public int ParameterCount { get; set; }
}

/// <summary>Stage 5: one dynamic item + its static correlation result.</summary>
public class CorrelatedCodeEvidence
{
    public DynamicLocationEvidence Dynamic { get; set; } = new();
    public bool IsCorrelated { get; set; }
    public CorrelationMatchStrategy Strategy { get; set; } = CorrelationMatchStrategy.None;
    public double MatchConfidence { get; set; }
    public List<StaticMethodIndexEntry> MatchedMethods { get; set; } = new();
    public string? PrimarySymbolKey { get; set; }
    public string? PrimaryDisplayName { get; set; }
}

/// <summary>Aggregated correlation report — feeds BF-14 Evidence Fusion.</summary>
public class StaticDynamicCorrelationReport
{
    public List<CorrelatedCodeEvidence> Items { get; set; } = new();

    public int TotalDynamicEvidence { get; set; }
    public int TotalCorrelated { get; set; }
    public int TotalUncorrelated { get; set; }
    public double CorrelationRate { get; set; }      // correlated / total
    public int StaticIndexSize { get; set; }

    /// <summary>Static symbols that received dynamic support — the strongest
    /// investigation signal for BF-14 (Static Evidence ↔ Dynamic Evidence).</summary>
    public List<string> SymbolKeysWithDynamicSupport { get; set; } = new();

    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}