using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-15.7: Experimental Fault Localization Models
/// Research-only capability (L16). Reuses ExecutionSpectrum (BF-12) -
/// no duplicate spectrum model by design.
/// </summary>

public class ExperimentalSuspiciousness
{
    public string ElementId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? MethodName { get; set; }
    public int? LineNumber { get; set; }
    public double Score { get; set; }
    public int Rank { get; set; }
}

public class ExperimentalLocalizationResult
{
    public string AlgorithmName { get; set; } = string.Empty;
    public int TotalElements { get; set; }
    public List<ExperimentalSuspiciousness> Entries { get; set; } = new();
    public DateTime ComputedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Stage 3: one benchmark scenario with ground truth.</summary>
public class SbflBenchmarkScenario
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<ExecutionSpectrum> Spectra { get; set; } = new();

    public string? ExpectedTop1 { get; set; }             // single-fault scenarios
    public List<string>? ExpectedTop2Set { get; set; }    // two-fault scenarios
    public bool ExpectAllZero { get; set; }               // green-run degenerate case

    public string ExpectedDescription => ExpectAllZero ? "all scores zero"
        : ExpectedTop1 is not null ? $"top-1 = {ExpectedTop1}"
        : ExpectedTop2Set is not null ? $"top-2 = {{{string.Join(", ", ExpectedTop2Set)}}}"
        : "no expectation";
}

/// <summary>Stage 4: one (scenario x algorithm) evaluation row.</summary>
public class BenchmarkEvaluationRow
{
    public string Scenario { get; set; } = string.Empty;
    public string AlgorithmName { get; set; } = string.Empty;
    public string ExpectedDescription { get; set; } = string.Empty;
    public string ObservedTop1 { get; set; } = string.Empty;
    public bool GroundTruthHit { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class AlgorithmBenchmarkSummary
{
    public string AlgorithmName { get; set; } = string.Empty;
    public int ScenariosPassed { get; set; }
    public int TotalScenarios { get; set; }
    public double PassRate => TotalScenarios == 0 ? 0.0 : (double)ScenariosPassed / TotalScenarios;
}

/// <summary>Stage 5: preserved experimental evidence.</summary>
public class ExperimentalComparisonReport
{
    public List<BenchmarkEvaluationRow> Rows { get; set; } = new();
    public List<AlgorithmBenchmarkSummary> Summaries { get; set; } = new();
    public List<string> BaselineNotes { get; set; } = new();
    public bool IsExperimental { get; set; } = true;
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;

    public const string DisclaimerText =
     "EXPERIMENTAL (BF-15.7): research evidence only - not production guidance. " +
     "Suspiciousness != Root Cause. Results must not gate releases.";

    /// <summary>Serialization view of the disclaimer (consts are invisible to System.Text.Json).</summary>
    public string Disclaimer => DisclaimerText;
}