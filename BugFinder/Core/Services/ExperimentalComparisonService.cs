using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-15.7 Stages 3-5: benchmark dataset, comparison, evidence preservation.
/// Ground truth kinds: ExpectedTop1 / ExpectedTop2Set / ExpectAllZero.
/// Misses are recorded as evidence rows - never thrown.
/// </summary>
public class ExperimentalComparisonService
{
    public static IReadOnlyList<IExperimentalFaultLocalizer> DefaultAlgorithms { get; } =
        new IExperimentalFaultLocalizer[]
        {
            new DStarLocalizer(), new Wong3Localizer(), new Op2Localizer(), new OchiaiReferenceLocalizer()
        };

    // Stage 3 — deterministic benchmark dataset (consistent test-run totals)
    public static IReadOnlyList<SbflBenchmarkScenario> BuildDefaultDataset() => new List<SbflBenchmarkScenario>
    {
        new SbflBenchmarkScenario
        {
            Name = "PerfectIsolation",
            Description = "single fault, zero innocent overlap - top-1 must be the fault",
            ExpectedTop1 = "Fault1",
            Spectra = new List<ExecutionSpectrum>   // TF=3, TP=3
            {
                new() { ElementId = "Fault1",    FilePath = "Calc.cs", FailedCount = 3, FailSkipCount = 0, PassedCount = 0, PassSkipCount = 3 },
                new() { ElementId = "InnocentA", FilePath = "Calc.cs", FailedCount = 0, FailSkipCount = 3, PassedCount = 2, PassSkipCount = 1 },
                new() { ElementId = "InnocentB", FilePath = "Calc.cs", FailedCount = 0, FailSkipCount = 3, PassedCount = 1, PassSkipCount = 2 }
            }
        },
        new SbflBenchmarkScenario
        {
            Name = "NoisyFault",
            Description = "single fault covered by one passing test - still must lead",
            ExpectedTop1 = "Fault1",
            Spectra = new List<ExecutionSpectrum>   // TF=4, TP=2
            {
                new() { ElementId = "Fault1",    FilePath = "Calc.cs", FailedCount = 4, FailSkipCount = 0, PassedCount = 1, PassSkipCount = 1 },
                new() { ElementId = "InnocentA", FilePath = "Calc.cs", FailedCount = 0, FailSkipCount = 4, PassedCount = 1, PassSkipCount = 1 },
                new() { ElementId = "InnocentB", FilePath = "Calc.cs", FailedCount = 0, FailSkipCount = 4, PassedCount = 2, PassSkipCount = 0 }
            }
        },
        new SbflBenchmarkScenario
        {
            Name = "TwoFaults",
            Description = "two faults (one noisy) - both must occupy top-2",
            ExpectedTop2Set = new List<string> { "Fault1", "Fault2" },
            Spectra = new List<ExecutionSpectrum>   // TF=5, TP=2
            {
                new() { ElementId = "Fault1",    FilePath = "Calc.cs", FailedCount = 5, FailSkipCount = 0, PassedCount = 0, PassSkipCount = 2 },
                new() { ElementId = "Fault2",    FilePath = "Calc.cs", FailedCount = 4, FailSkipCount = 1, PassedCount = 1, PassSkipCount = 1 },
                new() { ElementId = "InnocentA", FilePath = "Calc.cs", FailedCount = 0, FailSkipCount = 5, PassedCount = 1, PassSkipCount = 1 }
            }
        },
        new SbflBenchmarkScenario
        {
            Name = "NoFailureRun",
            Description = "green run - every score must be zero (degenerate spectrum)",
            ExpectAllZero = true,
            Spectra = new List<ExecutionSpectrum>   // TF=0, TP=3
            {
                new() { ElementId = "X", FilePath = "Calc.cs", FailedCount = 0, FailSkipCount = 0, PassedCount = 2, PassSkipCount = 1 },
                new() { ElementId = "Y", FilePath = "Calc.cs", FailedCount = 0, FailSkipCount = 0, PassedCount = 3, PassSkipCount = 0 }
            }
        }
    };

    // Stage 4 — compare algorithms against ground truth
    public ExperimentalComparisonReport Compare(
        IReadOnlyList<SbflBenchmarkScenario>? scenarios,
        IReadOnlyList<IExperimentalFaultLocalizer>? algorithms = null)
    {
        var report = new ExperimentalComparisonReport();
        scenarios ??= BuildDefaultDataset();
        algorithms ??= DefaultAlgorithms;

        foreach (var algorithm in algorithms)
        {
            var passed = 0;
            foreach (var scenario in scenarios)
            {
                var row = Evaluate(scenario, algorithm);
                report.Rows.Add(row);
                if (row.GroundTruthHit) passed++;
            }
            report.Summaries.Add(new AlgorithmBenchmarkSummary
            {
                AlgorithmName = algorithm.AlgorithmName,
                ScenariosPassed = passed,
                TotalScenarios = scenarios.Count
            });
        }

        report.BaselineNotes.Add(
            "Ochiai(reference) is computed in-harness for benchmark purposes only; " +
            "the production SBFL pipeline remains BF-12.4-12.6.");
        report.Summaries = report.Summaries
            .OrderByDescending(s => s.PassRate)
            .ThenBy(s => s.AlgorithmName, StringComparer.Ordinal)
            .ToList();
        return report;
    }

    private static BenchmarkEvaluationRow Evaluate(SbflBenchmarkScenario scenario, IExperimentalFaultLocalizer algorithm)
    {
        var result = algorithm.Localize(scenario.Spectra);
        var top1 = result.Entries.FirstOrDefault()?.ElementId ?? string.Empty;

        var (hit, notes) = scenario.ExpectAllZero
            ? EvaluateAllZero(result)
            : scenario.ExpectedTop1 is not null
                ? (top1 == scenario.ExpectedTop1, $"top-1 observed = '{top1}'")
                : EvaluateTop2(scenario, result);

        return new BenchmarkEvaluationRow
        {
            Scenario = scenario.Name,
            AlgorithmName = algorithm.AlgorithmName,
            ExpectedDescription = scenario.ExpectedDescription,
            ObservedTop1 = top1,
            GroundTruthHit = hit,
            Notes = notes
        };
    }

    private static (bool Hit, string Notes) EvaluateAllZero(ExperimentalLocalizationResult result)
    {
        var allZero = result.Entries.All(e => e.Score == 0.0);
        return (allZero, allZero ? "all scores zero as expected" : "non-zero score on a green run");
    }

    private static (bool Hit, string Notes) EvaluateTop2(SbflBenchmarkScenario scenario, ExperimentalLocalizationResult result)
    {
        var expected = scenario.ExpectedTop2Set ?? new List<string>();
        var actual = result.Entries.Take(2).Select(e => e.ElementId).ToList();
        var hit = expected.Count == 2 && actual.All(expected.Contains) && expected.All(actual.Contains);
        return (hit, $"top-2 observed = {{{string.Join(", ", actual)}}}");
    }

    // Stage 5 — preserve evidence as JSON string (file persistence: future CLI)
    public string ToJson(ExperimentalComparisonReport report)
    {
        if (report is null) throw new ArgumentNullException(nameof(report));
        return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    }
}