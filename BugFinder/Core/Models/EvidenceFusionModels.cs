using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-14.1: Evidence Fusion Models
/// Collects evidence from all Bug Finder phases, normalizes, weights per
/// source, and fuses into one corroborated evidence package per target.
/// Source vocabulary = CandidateEvidenceType (extended in 14.1) — no
/// duplicate enum by design.
/// </summary>

/// <summary>Stage 1/2 input: one raw evidence item pointing at a target.</summary>
public class FusionEvidenceInput
{
    public CandidateEvidenceType SourceType { get; set; }
    public double RawStrength { get; set; }              // clamped to [0,1]
    public string? TargetSymbolKey { get; set; }         // BF-13.4 StableKey (preferred)
    public string? TargetFilePath { get; set; }
    public int? TargetLineNumber { get; set; }
    public string? SourceArtifact { get; set; }          // TRX / coverage file / SHA / ElementId
    public string? Description { get; set; }
}

/// <summary>Stage 3: per-source weight configuration.</summary>
public class EvidenceWeightConfig
{
    private readonly Dictionary<CandidateEvidenceType, double> _weights = new();

    public double DefaultWeight { get; set; } = 1.0;

    public void SetWeight(CandidateEvidenceType source, double weight)
        => _weights[source] = Math.Max(0.01, weight);    // guard: no zero-total division

    public double GetWeight(CandidateEvidenceType source)
        => _weights.TryGetValue(source, out var weight) ? weight : DefaultWeight;
}

/// <summary>Stage 4/5: one fused target with its combined evidence.</summary>
public class FusedEvidenceItem
{
    public string TargetKey { get; set; } = string.Empty;   // symbol key or FILE|path
    public string? TargetSymbolKey { get; set; }
    public string? TargetFilePath { get; set; }
    public int? TargetLineNumber { get; set; }

    public double FusedStrength { get; set; }        // weighted combination
    public double MaxSingleStrength { get; set; }
    public int SignalCount { get; set; }             // contributing inputs
    public int DistinctSourceCount { get; set; }     // distinct source types
    public List<CandidateEvidenceType> Sources { get; set; } = new();
}

/// <summary>Stage 5: fused evidence package — input contract for BF-14.2+.</summary>
public class EvidenceFusionReport
{
    public List<FusedEvidenceItem> Items { get; set; } = new();
    public int TotalInputs { get; set; }
    public int TotalTargets { get; set; }
    public int FullyCorroboratedCount { get; set; }  // targets backed by >= 2 distinct sources
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}