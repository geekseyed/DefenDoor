using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-12.5: Tarantula Fault Localization Service
/// Implements the classic Tarantula algorithm based on failure/success density ratios.
/// Formula: (Failed(e)/TotalFailed) / ( (Failed(e)/TotalFailed) + (Passed(e)/TotalPassed) )
/// </summary>
public class TarantulaFaultLocalizationService
{
    private readonly TarantulaConfig _config;

    public TarantulaFaultLocalizationService(TarantulaConfig? config = null)
    {
        _config = config ?? new TarantulaConfig();
    }

    /// <summary>
    /// BF-12.5 - Main Entry Point:
    /// Calculates Tarantula suspiciousness scores for a given spectrum.
    /// </summary>
    public TarantulaResult Calculate(List<ExecutionSpectrum> spectra, int totalPassed, int totalFailed)
    {
        var result = new TarantulaResult
        {
            TotalElementsAnalyzed = spectra.Count
        };

        if (totalFailed == 0 || totalPassed == 0)
        {
            result.WarningMessage = "Tarantula requires both passed and failed tests to calculate ratios.";
            return result;
        }

        var locations = new List<SuspiciousLocation>();

        foreach (var s in spectra)
        {
            // Skip if never executed by any test
            if (s.PassedCount == 0 && s.FailedCount == 0)
                continue;

            double score = CalculateRawScore(s.FailedCount, s.PassedCount, totalFailed, totalPassed);

            var loc = new SuspiciousLocation
            {
                ElementId = s.ElementId,
                FilePath = s.FilePath,
                MethodName = s.MethodName,
                LineNumber = s.LineNumber,
                FinalSuspiciousness = _config.NormalizeScores ? Math.Min(1.0, score) : score,
                TarantulaScore = score, // Store raw score as well
                ExecutingFailedTests = s.FailedCount,
                ExecutingPassedTests = s.PassedCount
            };

            // Flag high suspicion based on config
            if (loc.FinalSuspiciousness >= _config.HighSuspicionThreshold)
            {
                // Metadata tagging could be added here if needed in future
            }

            locations.Add(loc);
        }

        // Rank: Higher score = More Suspicious
        result.RankedLocations = locations
            .OrderByDescending(l => l.FinalSuspiciousness)
            .ThenByDescending(l => l.ExecutingFailedTests)
            .ToList();

        if (result.RankedLocations.Any())
        {
            result.MaxScoreAchieved = result.RankedLocations.First().FinalSuspiciousness;
        }

        return result;
    }

    /// <summary>
    /// Core Tarantula Formula Implementation.
    /// </summary>
    private double CalculateRawScore(int failedExec, int passedExec, int totalFailed, int totalPassed)
    {
        // Avoid division by zero
        if (totalFailed == 0 || totalPassed == 0) return 0.0;

        double failedRatio = (double)failedExec / totalFailed;
        double passedRatio = (double)passedExec / totalPassed;

        double denominator = failedRatio + passedRatio;

        if (denominator == 0) return 0.0;

        return failedRatio / denominator;
    }
}