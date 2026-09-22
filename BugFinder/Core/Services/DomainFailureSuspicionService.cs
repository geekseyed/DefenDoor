using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-12.8: Domain Failure Suspicion Service
/// Enhances SBFL scores by injecting domain evaluation context.
/// Logic: A line covered by a failing Domain Check is more suspicious than one covered only by a failing Unit Test.
/// </summary>
public class DomainFailureSuspicionService
{
    private readonly DomainWeightConfig _config;

    public DomainFailureSuspicionService(DomainWeightConfig? config = null)
    {
        _config = config ?? new DomainWeightConfig();
    }

    /// <summary>
    /// BF-12.8 - Main Entry Point:
    /// Takes standard spectra and domain failure data, returns weighted spectra.
    /// </summary>
    public List<WeightedExecutionSpectrum> EnhanceWithDomainData(
        List<ExecutionSpectrum> baseSpectra,
        List<DomainFailureSpectrum> domainFailures)
    {
        if (baseSpectra == null || !baseSpectra.Any())
            return new List<WeightedExecutionSpectrum>();

        // 1. Map domain failures to lines for quick lookup
        // Key: "File.cs:Line", Value: List of (Severity, Count)
        var domainMap = new Dictionary<string, List<(string Severity, int Count)>>();

        if (domainFailures != null)
        {
            foreach (var failure in domainFailures)
            {
                foreach (var line in failure.CoveredLines)
                {
                    if (!domainMap.ContainsKey(line))
                        domainMap[line] = new List<(string, int)>();

                    domainMap[line].Add((failure.Severity, 1));
                }
            }
        }

        // 2. Create Weighted Spectra
        var weightedList = new List<WeightedExecutionSpectrum>();

        foreach (var baseSpec in baseSpectra)
        {
            var weightedSpec = new WeightedExecutionSpectrum
            {
                ElementId = baseSpec.ElementId,
                FilePath = baseSpec.FilePath,
                MethodName = baseSpec.MethodName,
                LineNumber = baseSpec.LineNumber,
                PassedCount = baseSpec.PassedCount,
                FailedCount = baseSpec.FailedCount,
                PassSkipCount = baseSpec.PassSkipCount,
                FailSkipCount = baseSpec.FailSkipCount,
                DomainFailureCount = 0,
                DomainSuccessCount = 0, // Would need domain success input to fill accurately, assuming 0 for now
                DomainWeightFactor = 1.0
            };

            // Apply Domain Logic
            if (domainMap.ContainsKey(baseSpec.ElementId))
            {
                var hits = domainMap[baseSpec.ElementId];
                weightedSpec.DomainFailureCount = hits.Count;

                // Calculate Weight Factor based on highest severity found
                double maxBonus = 0.0;
                foreach (var hit in hits)
                {
                    double bonus = hit.Severity.ToLower() switch
                    {
                        "critical" => _config.CriticalSeverityBonus,
                        "high" => _config.HighSeverityBonus,
                        "medium" => _config.MediumSeverityBonus,
                        _ => 0.0
                    };
                    if (bonus > maxBonus) maxBonus = bonus;
                }

                weightedSpec.DomainWeightFactor = _config.BaseDomainMultiplier + maxBonus;

                // Isolation Bonus: If executed by failing domain checks but NO passing tests
                if (weightedSpec.PassedCount == 0 && weightedSpec.DomainFailureCount > 0)
                {
                    weightedSpec.DomainWeightFactor += _config.IsolationBonus;
                }
            }

            weightedList.Add(weightedSpec);
        }

        return weightedList;
    }

    /// <summary>
    /// Recalculates the Ochiai score using the Domain Weight Factor.
    /// Formula: (Standard_Ochiai * DomainWeightFactor) capped at 1.0
    /// </summary>
    public void ApplyWeightToScores(List<WeightedExecutionSpectrum> weightedSpectra, int totalFailedTests)
    {
        foreach (var spec in weightedSpectra)
        {
            // 1. Calculate Base Ochiai
            double baseScore = 0.0;
            double failedExecuted = spec.FailedCount;
            double totalFailed = totalFailedTests;
            double passedExecuted = spec.PassedCount;

            double denominator = Math.Sqrt((failedExecuted + (totalFailed - failedExecuted)) * (failedExecuted + passedExecuted));
            if (denominator > 0)
            {
                baseScore = failedExecuted / denominator;
            }

            // 2. Apply Weight
            // Note: We treat Domain Failures as "Super Failures". 
            // If DomainFailureCount > 0, we boost the score.
            if (spec.DomainFailureCount > 0)
            {
                baseScore *= spec.DomainWeightFactor;
                if (baseScore > 1.0) baseScore = 1.0; // Cap at 1.0
            }

            spec.AdjustedSuspiciousness = baseScore;
        }
    }
}