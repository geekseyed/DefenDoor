using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-15.1 Stage 3: Mutant Execution CONTRACT. Real executors live
/// OUTSIDE the Core (they must modify/compile code - forbidden by the
/// Strict Core Boundary, BF-14.9). The Core ships only a deterministic
/// simulator for research/benchmark use. Stage 4: Mutant Detection.
/// </summary>
public interface IMutantExecutionGateway
{
    MutationExecutionSource Source { get; }
    MutationKillMatrix Execute(MutationScenario scenario, IReadOnlyList<MutantCandidate> plan);
}

/// <summary>
/// Deterministic research simulator (no source access, no compilation):
/// - StatementDeletion kills EVERY executing test (maximal behavior change);
/// - value-level operators are assumed detected only by FAILING
///   executing tests (conservative approximation).
/// Real kill data must come from an external IMutantExecutionGateway.
/// </summary>
public class SimulatedMutantExecutionGateway : IMutantExecutionGateway
{
    public MutationExecutionSource Source => MutationExecutionSource.Simulated;

    public MutationKillMatrix Execute(MutationScenario scenario, IReadOnlyList<MutantCandidate> plan)
    {
        if (scenario is null) throw new ArgumentNullException(nameof(scenario));
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        var testsByElement = scenario.Elements
            .ToDictionary(e => e.ElementId, e => e.ExecutingTestIds);
        var failedIds = scenario.Tests.Where(t => t.Failed)
            .Select(t => t.TestId).ToHashSet();

        var matrix = new MutationKillMatrix
        {
            ScenarioName = scenario.ScenarioName,
            Source = Source,
            Tests = scenario.Tests.ToList()
        };

        foreach (var mutant in plan)
        {
            testsByElement.TryGetValue(mutant.ElementId, out var executing);
            executing ??= new List<string>();

            var killers = mutant.Operator == MutationOperatorKind.StatementDeletion
                ? executing.OrderBy(id => id, StringComparer.Ordinal).ToList()
                : executing.Where(id => failedIds.Contains(id))
                           .OrderBy(id => id, StringComparer.Ordinal).ToList();

            matrix.Entries.Add(new MutantKillEntry
            {
                Candidate = mutant,
                KilledByTestIds = killers
            });
        }
        return matrix;
    }
}

/// <summary>Stage 4: killed vs survived classification.</summary>
public static class MutantDetectionService
{
    public static MutantDetectionSummary Detect(MutationKillMatrix? matrix)
    {
        var summary = new MutantDetectionSummary();
        if (matrix is null) return summary;

        summary.TotalMutants = matrix.Entries.Count;
        summary.KilledCount = matrix.Entries.Count(e => e.IsKilled);
        summary.SurvivedCount = summary.TotalMutants - summary.KilledCount;
        summary.KillRate = summary.TotalMutants == 0
            ? 0.0
            : (double)summary.KilledCount / summary.TotalMutants;
        summary.SurvivorMutantIds = matrix.Entries
            .Where(e => !e.IsKilled)
            .Select(e => e.Candidate.MutantId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        return summary;
    }
}