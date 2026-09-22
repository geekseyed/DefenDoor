using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-12.1: Spectrum-Based Fault Localization Service
/// Implements Ochiai, Tarantula, and Jaccard formulas to rank code elements by suspiciousness.
/// NOTE: Suspiciousness != Root Cause. This provides a ranked list of candidates.
/// </summary>
public class SpectrumBasedFaultLocalizationService
{
    /// <summary>
    /// BF-12.1 - Main Entry Point:
    /// Takes coverage spectra and test results to produce a ranked list of suspicious locations.
    /// </summary>
    public FaultLocalizationReport Analyze(
        List<ExecutionSpectrum> spectra,
        int totalPassedTests,
        int totalFailedTests,
        SbflAlgorithm algorithm = SbflAlgorithm.Hybrid)
    {
        var report = new FaultLocalizationReport
        {
            PrimaryAlgorithm = algorithm,
            TotalPassedTests = totalPassedTests,
            TotalFailedTests = totalFailedTests,
            TotalElementsAnalyzed = spectra.Count
        };

        if (totalFailedTests == 0)
        {
            report.WarningMessage = "No failed tests provided. SBFL requires at least one failure.";
            return report;
        }

        var suspiciousLocations = new List<SuspiciousLocation>();

        foreach (var spectrum in spectra)
        {
            // Skip elements never executed by any test (cannot be the cause if not run)
            if (spectrum.PassedCount == 0 && spectrum.FailedCount == 0)
                continue;

            var location = new SuspiciousLocation
            {
                ElementId = spectrum.ElementId,
                FilePath = spectrum.FilePath,
                MethodName = spectrum.MethodName,
                LineNumber = spectrum.LineNumber,
                ExecutingFailedTests = spectrum.FailedCount,
                ExecutingPassedTests = spectrum.PassedCount
            };

            // Calculate Scores
            location.OchiaiScore = CalculateOchiai(spectrum, totalFailedTests);
            location.TarantulaScore = CalculateTarantula(spectrum, totalPassedTests, totalFailedTests);
            location.JaccardScore = CalculateJaccard(spectrum, totalFailedTests);

            // Determine Final Score based on selected algorithm
            location.FinalSuspiciousness = algorithm switch
            {
                SbflAlgorithm.Ochiai => location.OchiaiScore,
                SbflAlgorithm.Tarantula => location.TarantulaScore,
                SbflAlgorithm.Jaccard => location.JaccardScore,
                SbflAlgorithm.Hybrid => (location.OchiaiScore * 0.5) + (location.TarantulaScore * 0.25) + (location.JaccardScore * 0.25),
                _ => location.OchiaiScore
            };

            suspiciousLocations.Add(location);
        }

        // Rank: Highest score = Rank 1
        var ranked = suspiciousLocations
            .OrderByDescending(l => l.FinalSuspiciousness)
            .ThenByDescending(l => l.ExecutingFailedTests) // Tie-breaker
            .ToList();

        for (int i = 0; i < ranked.Count; i++)
        {
            ranked[i].Rank = i + 1;
        }

        report.RankedLocations = ranked;
        return report;
    }

    // --- Formulas Implementation ---

    /// <summary>
    /// Ochiai Formula: 
    /// Failed(a,e) / sqrt( (Failed(a,e) + Failed(a)) * (Failed(a,e) + Passed(a,e)) )
    /// </summary>
    private double CalculateOchiai(ExecutionSpectrum s, int totalFailed)
    {
        double failedExecuted = s.FailedCount;
        double failedTotal = totalFailed;
        double passedExecuted = s.PassedCount;

        double denominator = Math.Sqrt((failedExecuted + (failedTotal - failedExecuted)) * (failedExecuted + passedExecuted));

        if (denominator == 0) return 0.0;
        return failedExecuted / denominator;
    }

    /// <summary>
    /// Tarantula Formula:
    /// (Failed(e)/TotalFailed) / ( (Failed(e)/TotalFailed) + (Passed(e)/TotalPassed) )
    /// </summary>
    private double CalculateTarantula(ExecutionSpectrum s, int totalPassed, int totalFailed)
    {
        if (totalFailed == 0 || totalPassed == 0) return 0.0;

        double failedRatio = (double)s.FailedCount / totalFailed;
        double passedRatio = (double)s.PassedCount / totalPassed;

        double denominator = failedRatio + passedRatio;
        if (denominator == 0) return 0.0;

        return failedRatio / denominator;
    }

    /// <summary>
    /// Jaccard Formula:
    /// Failed(e) / (Failed(e) + Passed(e) + Failed(!e))
    /// Note: Failed(!e) is totalFailed - Failed(e)
    /// Simplified: Failed(e) / (Passed(e) + TotalFailed)
    /// </summary>
    private double CalculateJaccard(ExecutionSpectrum s, int totalFailed)
    {
        double failedExecuted = s.FailedCount;
        double passedExecuted = s.PassedCount;

        double denominator = passedExecuted + totalFailed;
        if (denominator == 0) return 0.0;

        return failedExecuted / denominator;
    }
}