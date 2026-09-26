using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-15.2: Mutation Testing Integration Models (research only).
/// Integrates BF-15.1 kill data into per-test mutation power,
/// weak-test findings, and per-element mutation scores.
/// </summary>

public class TestMutationPower
{
    public string TestId { get; set; } = string.Empty;
    public int ExecutedMutantCount { get; set; }
    public int KilledMutantCount { get; set; }
    public double KillingRatio { get; set; }          // killed / executed (0 when none)
    public bool IsWeak => ExecutedMutantCount > 0 && KilledMutantCount == 0;
}

public class WeakTestFinding
{
    public string TestId { get; set; } = string.Empty;
    public int ExecutedMutantCount { get; set; }
    public List<string> ExecutedMutantIds { get; set; } = new();
    public string Reason { get; set; } = string.Empty;
}

public class ElementMutationScore
{
    public string ElementId { get; set; } = string.Empty;
    public int MutantCount { get; set; }
    public int KilledCount { get; set; }
    public double MutationScore { get; set; }
}

public class MutationIntegrationReport
{
    public string ScenarioName { get; set; } = string.Empty;
    public MutationExecutionSource Source { get; set; }

    public List<TestMutationPower> TestPowers { get; set; } = new();
    public List<WeakTestFinding> WeakTests { get; set; } = new();
    public List<ElementMutationScore> ElementScores { get; set; } = new();

    public int TotalMutants { get; set; }
    public int KilledMutants { get; set; }
    public int SurvivedMutants { get; set; }
    public double OverallMutationScore { get; set; }

    public bool IsExperimental { get; set; } = true;
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;

    public const string DisclaimerText =
        "EXPERIMENTAL (BF-15.2): mutation-testing research evidence - not production guidance. " +
        "Weak-test findings are leads, not verdicts. Results must not gate releases.";

    /// <summary>Serializable view (const strings are invisible to System.Text.Json - 15.7 lesson).</summary>
    public string Disclaimer => DisclaimerText;
}