using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-12.7: Multi-Test Aggregation Service
/// Aggregates suspiciousness scores from multiple failing tests to produce a unified ranking.
/// Essential for real-world scenarios where multiple tests fail simultaneously.
/// </summary>
public class MultiTestAggregationService
{
    /// <summary>
    /// BF-12.7 - Main Entry Point:
    /// Combines individual SBFL reports into a single aggregated report.
    /// </summary>
    /// <param name="individualReports">List of reports, one per failing test (or per algorithm run)</param>
    /// <param name="strategy">The aggregation strategy to apply</param>
    public MultiTestAggregationReport Aggregate(
        List<FaultLocalizationReport> individualReports,
        AggregationStrategy strategy = AggregationStrategy.Max)
    {
        var result = new MultiTestAggregationReport
        {
            StrategyUsed = strategy,
            TotalFailuresAnalyzed = individualReports.Count,
            GeneratedAt = DateTime.UtcNow
        };

        if (individualReports == null || !individualReports.Any())
        {
            return result;
        }

        // Dictionary to collect all scores for each unique element
        var elementScores = new Dictionary<string, List<double>>();
        var elementMetadata = new Dictionary<string, AggregatedSuspicionResult>();

        foreach (var report in individualReports)
        {
            foreach (var location in report.RankedLocations)
            {
                if (!elementScores.ContainsKey(location.ElementId))
                {
                    elementScores[location.ElementId] = new List<double>();
                    elementMetadata[location.ElementId] = new AggregatedSuspicionResult
                    {
                        ElementId = location.ElementId,
                        FilePath = location.FilePath,
                        LineNumber = location.LineNumber
                    };
                }

                // Use the FinalSuspiciousness from the individual report
                elementScores[location.ElementId].Add(location.FinalSuspiciousness);
            }
        }

        // Calculate Aggregated Scores
        foreach (var kvp in elementScores)
        {
            var elementId = kvp.Key;
            var scores = kvp.Value;
            var metadata = elementMetadata[elementId];

            metadata.ContributingFailureCount = scores.Count;
            metadata.MaxScore = scores.Max();
            metadata.AvgScore = scores.Average();

            metadata.FinalScore = strategy switch
            {
                AggregationStrategy.Max => metadata.MaxScore,
                AggregationStrategy.Average => metadata.AvgScore,
                AggregationStrategy.Sum => scores.Sum(),
                AggregationStrategy.Min => scores.Min(),
                _ => metadata.MaxScore
            };

            // Normalize Sum to 0-1 range if necessary (optional, here we allow >1 for Sum)
            if (strategy == AggregationStrategy.Sum && metadata.FinalScore > 1.0)
            {
                // Optional: Normalize or leave as cumulative weight. 
                // For ranking, cumulative weight is often useful.
            }
        }

        // Rank Results
        var rankedList = elementMetadata.Values
            .OrderByDescending(x => x.FinalScore)
            .ThenByDescending(x => x.ContributingFailureCount)
            .ToList();

        for (int i = 0; i < rankedList.Count; i++)
        {
            rankedList[i].FinalRank = i + 1;
        }

        result.RankedResults = rankedList;
        result.TotalElementsRanked = rankedList.Count;

        return result;
    }
}