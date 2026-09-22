using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-12.6: Jaccard Fault Localization Service
/// Implements the Jaccard coefficient: Failed(e) / (Passed(e) + TotalFailed)
/// Excellent for isolating bugs triggered by specific, rare conditions.
/// </summary>
public class JaccardFaultLocalizationService
{
    /// <summary>
    /// BF-12.6 - Main Entry Point:
    /// Calculates Jaccard scores for all spectrum elements.
    /// </summary>
    public JaccardReport Calculate(
        List<ExecutionSpectrum> spectra,
        int totalFailedTests,
        JaccardConfig? config = null)
    {
        config ??= new JaccardConfig();
        var report = new JaccardReport { TotalElements = spectra.Count };

        if (totalFailedTests == 0 || !spectra.Any())
        {
            return report;
        }

        var results = new List<JaccardResult>();

        foreach (var s in spectra)
        {
            // Optimization: If never executed by a failed test, score is 0
            if (s.FailedCount == 0) continue;

            double score = CalculateJaccardScore(s.FailedCount, s.PassedCount, totalFailedTests);

            if (score < config.MinimumScoreThreshold) continue;

            results.Add(new JaccardResult
            {
                ElementId = s.ElementId,
                FilePath = s.FilePath,
                LineNumber = s.LineNumber,
                JaccardScore = score,
                ExecutedByFailed = s.FailedCount,
                ExecutedByPassed = s.PassedCount,
                TotalFailedTests = totalFailedTests
            });
        }

        // Ranking
        var ranked = results.OrderByDescending(r => r.JaccardScore).ToList();

        for (int i = 0; i < ranked.Count; i++)
        {
            ranked[i].Rank = i + 1;
        }

        report.RankedResults = config.TopNResults > 0
            ? ranked.Take(config.TopNResults).ToList()
            : ranked;

        return report;
    }

    /// <summary>
    /// Jaccard Formula: aef / (aep + anf + aef) -> simplifies to aef / (aep + TotalFailed)
    /// </summary>
    private double CalculateJaccardScore(int failedExecuted, int passedExecuted, int totalFailed)
    {
        double numerator = failedExecuted;
        double denominator = passedExecuted + totalFailed;

        if (denominator == 0) return 0.0;
        return numerator / denominator;
    }
}