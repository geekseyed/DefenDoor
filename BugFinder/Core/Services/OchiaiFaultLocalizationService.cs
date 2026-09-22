using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-12.4: Dedicated Ochiai Fault Localization Service
/// Implements the Ochiai coefficient, widely considered the most effective SBFL formula.
/// Formula: aef / sqrt( (aef + anf) * (aef + aep) )
/// Where:
///   aef = Failed tests executing the element
///   anf = Failed tests NOT executing the element (TotalFailed - aef)
///   aep = Passed tests executing the element
/// </summary>
public class OchiaiFaultLocalizationService
{
    /// <summary>
    /// BF-12.4 - Main Entry Point:
    /// Analyzes execution spectra using purely the Ochiai formula.
    /// </summary>
    public OchiaiLocalizationReport Analyze(
        List<ExecutionSpectrum> spectra,
        int totalPassedTests,
        int totalFailedTests)
    {
        var report = new OchiaiLocalizationReport
        {
            TotalPassedTests = totalPassedTests,
            TotalFailedTests = totalFailedTests,
            TotalElementsAnalyzed = spectra.Count
        };

        if (totalFailedTests == 0)
        {
            report.WarningMessage = "Ochiai requires at least one failed test to calculate suspiciousness.";
            return report;
        }

        var results = new List<OchiaiResult>();

        foreach (var s in spectra)
        {
            // Optimization: If a line is not executed by any failed test, its Ochiai score is 0.
            // We can skip it or include it as 0. Including it helps show the full landscape.
            if (s.FailedCount == 0)
            {
                results.Add(new OchiaiResult
                {
                    ElementId = s.ElementId,
                    FilePath = s.FilePath,
                    LineNumber = s.LineNumber,
                    OchiaiScore = 0.0,
                    FailedExecuted = 0,
                    PassedExecuted = s.PassedCount,
                    TotalFailed = totalFailedTests,
                    TotalPassed = totalPassedTests
                });
                continue;
            }

            double aef = s.FailedCount;
            double aep = s.PassedCount;
            double anf = totalFailedTests - s.FailedCount; // Failed but didn't execute

            // Denominator: sqrt( (aef + anf) * (aef + aep) )
            // Note: (aef + anf) is simply TotalFailedTests
            double term1 = totalFailedTests;
            double term2 = aef + aep;

            double denominator = Math.Sqrt(term1 * term2);

            double score = (denominator == 0) ? 0.0 : aef / denominator;

            results.Add(new OchiaiResult
            {
                ElementId = s.ElementId,
                FilePath = s.FilePath,
                LineNumber = s.LineNumber,
                OchiaiScore = score,
                FailedExecuted = s.FailedCount,
                PassedExecuted = s.PassedCount,
                TotalFailed = totalFailedTests,
                TotalPassed = totalPassedTests
            });
        }

        // Ranking: Descending order of score
        var ranked = results
            .OrderByDescending(r => r.OchiaiScore)
            .ThenByDescending(r => r.FailedExecuted) // Tie-breaker: More failed hits is worse
            .ToList();

        // Calculate Margins & Assign Ranks
        for (int i = 0; i < ranked.Count; i++)
        {
            ranked[i].Rank = i + 1;

            // Calculate margin to the next item (if exists)
            if (i < ranked.Count - 1)
            {
                ranked[i].ScoreMargin = ranked[i].OchiaiScore - ranked[i + 1].OchiaiScore;
            }
            else
            {
                ranked[i].ScoreMargin = 0.0;
            }
        }

        report.RankedResults = ranked;
        return report;
    }
}