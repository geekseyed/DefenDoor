using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-15.1: Mutation-Based Fault Localization Models (research only).
/// Mutants are PLANS over spectrum elements - the Core never modifies
/// source (Strict Core Boundary, BF-14.9). Real mutant execution belongs
/// to external gateways; the Core ships a deterministic simulator for
/// benchmarking the formulas.
/// </summary>

public enum MutationOperatorKind
{
    ArithmeticOperatorReplacement,
    RelationalOperatorReplacement,
    LogicalOperatorNegation,
    BoundaryConditionShift,
    ConstantReplacement,
    StatementDeletion
}

public static class MutationOperatorCatalog
{
    public static string Describe(MutationOperatorKind kind) => kind switch
    {
        MutationOperatorKind.ArithmeticOperatorReplacement => "replace an arithmetic operator with another",
        MutationOperatorKind.RelationalOperatorReplacement => "replace a relational operator with another",
        MutationOperatorKind.LogicalOperatorNegation => "negate a logical operator or boolean expression",
        MutationOperatorKind.BoundaryConditionShift => "shift a boundary condition by one (e.g. < to <=)",
        MutationOperatorKind.ConstantReplacement => "replace a literal constant with a nearby value",
        MutationOperatorKind.StatementDeletion => "remove the statement entirely",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}

public enum MutationExecutionSource { Simulated, Real }

/// <summary>A mutation PLAN entry - nothing is modified, ever.</summary>
public class MutantCandidate
{
    public string MutantId { get; set; } = string.Empty;
    public string ElementId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public MutationOperatorKind Operator { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class ResearchTestOutcome
{
    public string TestId { get; set; } = string.Empty;
    public bool Failed { get; set; }
}

public class ElementExecution
{
    public string ElementId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public List<string> ExecutingTestIds { get; set; } = new();
}

/// <summary>Per-test research scenario - richer than the aggregate ExecutionSpectrum.</summary>
public class MutationScenario
{
    public string ScenarioName { get; set; } = string.Empty;
    public List<ElementExecution> Elements { get; set; } = new();
    public List<ResearchTestOutcome> Tests { get; set; } = new();
    public List<MutationOperatorKind> Operators { get; set; } = new();
}

public class MutantKillEntry
{
    public MutantCandidate Candidate { get; set; } = new();
    public List<string> KilledByTestIds { get; set; } = new();
    public bool IsKilled => KilledByTestIds.Count > 0;
}

public class MutationKillMatrix
{
    public string ScenarioName { get; set; } = string.Empty;
    public MutationExecutionSource Source { get; set; }
    public List<ResearchTestOutcome> Tests { get; set; } = new();
    public List<MutantKillEntry> Entries { get; set; } = new();
}

public class MutantDetectionSummary
{
    public int TotalMutants { get; set; }
    public int KilledCount { get; set; }
    public int SurvivedCount { get; set; }
    public double KillRate { get; set; }
    public List<string> SurvivorMutantIds { get; set; } = new();
}