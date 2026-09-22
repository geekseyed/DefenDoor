using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-12.9: Multi-Signal Ranking Service
/// Fuses SBFL scores, Multi-Test Aggregation, and Domain Failure weights into a single unified ranking.
/// This is the final step before generating the Fault Localization Report for the user.
/// </summary>
public class MultiSignalRankingService
{
    private readonly SignalWeightConfig _config;

    public MultiSignalRankingService(SignalWeightConfig? config = null)
    {
        _config = config ?? new SignalWeightConfig();
    }

    /// <summary>
    /// BF-12.9 - Main Entry Point:
    /// Combines multiple signal sources into a unified score and ranks candidates.
    /// </summary>
    public MultiSignalRankingReport Rank(List<SignalInput> inputs)
    {
        var report = new MultiSignalRankingReport
        {
            WeightsUsed = _config
        };

        if (inputs == null || inputs.Count == 0)
        {
            return report;
        }

        var weights = _config.GetNormalizedWeights();
        var rankedCandidates = new List<RankedFaultCandidate>();

        foreach (var input in inputs)
        {
            double unifiedScore =
                (input.SbflScore * weights.sbfl) +
                (input.AggregatedScore * weights.agg) +
                (input.DomainFailureScore * weights.domain);

            unifiedScore = Math.Min(1.0, Math.Max(0.0, unifiedScore));

            rankedCandidates.Add(new RankedFaultCandidate
            {
                ElementId = input.ElementId,
                FilePath = input.FilePath,
                LineNumber = input.LineNumber,
                RawSbflScore = input.SbflScore,
                AggregatedScore = input.AggregatedScore,
                DomainScore = input.DomainFailureScore,
                UnifiedScore = unifiedScore,
                TotalFailures = input.TotalFailures,
                ConfidenceLevel = DetermineConfidence(unifiedScore)
            });
        }

        var sorted = rankedCandidates
            .OrderByDescending(c => c.UnifiedScore)
            .ThenByDescending(c => c.TotalFailures)
            .ToList();

        for (int i = 0; i < sorted.Count; i++)
        {
            sorted[i].Rank = i + 1;
        }

        report.Candidates = sorted;
        report.TotalCandidates = sorted.Count;

        return report;
    }

    private string DetermineConfidence(double score)
    {
        if (score >= 0.8) return "Very High";
        if (score >= 0.6) return "High";
        if (score >= 0.4) return "Medium";
        if (score >= 0.2) return "Low";
        return "Very Low";
    }
}