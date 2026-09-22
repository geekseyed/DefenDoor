using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-12.9: Multi-Signal Ranking Models
/// Defines the unified scoring model that fuses SBFL, Aggregation, and Domain signals.
/// </summary>

/// <summary>
/// Configuration for weighting different signals in the final ranking.
/// Weights should ideally sum to 1.0, but the system normalizes them automatically.
/// </summary>
public class SignalWeightConfig
{
    public double SbflWeight { get; set; } = 0.5;      // Importance of raw SBFL score (Ochiai/etc)
    public double AggregationWeight { get; set; } = 0.3; // Importance of multi-test consensus
    public double DomainWeight { get; set; } = 0.2;    // Importance of Domain Evaluation failure

    /// <summary>
    /// Helper to normalize weights so they sum to 1.0
    /// </summary>
    public (double sbfl, double agg, double domain) GetNormalizedWeights()
    {
        var total = SbflWeight + AggregationWeight + DomainWeight;
        if (total == 0) return (0.33, 0.33, 0.34); // Fallback equal weight

        return (SbflWeight / total, AggregationWeight / total, DomainWeight / total);
    }
}

/// <summary>
/// Input container holding all signal scores for a single code element.
/// </summary>
public class SignalInput
{
    public string ElementId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int? LineNumber { get; set; }

    public double SbflScore { get; set; }           // From BF-12.2/12.4/12.5/12.6
    public double AggregatedScore { get; set; }     // From BF-12.7
    public double DomainFailureScore { get; set; }  // From BF-12.8 (0.0 if no domain failure)

    public int TotalFailures { get; set; }          // Metadata for tie-breaking
}

/// <summary>
/// The final output of the Multi-Signal Ranking engine.
/// </summary>
public class RankedFaultCandidate
{
    public string ElementId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int? LineNumber { get; set; }

    // Component Scores (for transparency/debugging)
    public double RawSbflScore { get; set; }
    public double AggregatedScore { get; set; }
    public double DomainScore { get; set; }

    // The Final Unified Score
    public double UnifiedScore { get; set; }

    // Metadata (consensus tie-breaking)
    public int TotalFailures { get; set; }

    public int Rank { get; set; }
    public string ConfidenceLevel { get; set; } = string.Empty;
}

/// <summary>
/// Final report containing the ranked list of candidates.
/// </summary>
public class MultiSignalRankingReport
{
    public List<RankedFaultCandidate> Candidates { get; set; } = new();
    public SignalWeightConfig WeightsUsed { get; set; } = new();
    public int TotalCandidates { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}