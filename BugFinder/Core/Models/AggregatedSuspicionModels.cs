using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-12.7: Multi-Test Aggregation Models
/// Defines strategies and results for combining suspiciousness scores from multiple failing tests.
/// </summary>

/// <summary>
/// Strategy used to aggregate scores across multiple failures.
/// </summary>
public enum AggregationStrategy
{
    Max,        // Takes the highest score assigned by any failing test (Optimistic for localization)
    Average,    // Takes the mean score (Balanced)
    Sum,        // Sums up scores (Favors lines hit by many failures)
    Min         // Takes the lowest score (Pessimistic/Conservative)
}

/// <summary>
/// Represents the aggregated suspiciousness of a single code element.
/// </summary>
public class AggregatedSuspicionResult
{
    public string ElementId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int? LineNumber { get; set; }

    // The Final Aggregated Score
    public double FinalScore { get; set; }

    // Breakdown of scores from individual algorithms/tests (Optional diagnostic data)
    public double MaxScore { get; set; }
    public double AvgScore { get; set; }
    public int ContributingFailureCount { get; set; } // How many failing tests executed this line

    public int FinalRank { get; set; }
}

/// <summary>
/// Report containing the full list of aggregated suspicions.
/// </summary>
public class MultiTestAggregationReport
{
    public List<AggregatedSuspicionResult> RankedResults { get; set; } = new();
    public AggregationStrategy StrategyUsed { get; set; }
    public int TotalFailuresAnalyzed { get; set; }
    public int TotalElementsRanked { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}