using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-15.1 Stage 2: Mutation Generation.
/// Produces a deterministic mutant PLAN (elements x operators, MUT-### ids).
/// A plan only - no source is modified and nothing is written.
/// Also bridges the per-test research scenario back to the aggregate
/// ExecutionSpectrum (BF-12 model reuse).
/// </summary>
public class MutationGenerationService
{
    public List<MutantCandidate> GeneratePlan(MutationScenario scenario)
    {
        if (scenario is null) throw new ArgumentNullException(nameof(scenario));

        var plan = new List<MutantCandidate>();
        var counter = 0;

        foreach (var element in scenario.Elements.OrderBy(e => e.ElementId, StringComparer.Ordinal))
            foreach (var op in scenario.Operators)
            {
                counter++;
                plan.Add(new MutantCandidate
                {
                    MutantId = $"MUT-{counter:D3}",
                    ElementId = element.ElementId,
                    FilePath = element.FilePath,
                    Operator = op,
                    Description = $"{op} at '{element.ElementId}' - {MutationOperatorCatalog.Describe(op)}"
                });
            }
        return plan;
    }

    /// <summary>Aggregate spectrum derived from the per-test model (BF-12 reuse).</summary>
    public List<ExecutionSpectrum> DeriveSpectrum(MutationScenario scenario)
    {
        if (scenario is null) throw new ArgumentNullException(nameof(scenario));

        var spectrum = new List<ExecutionSpectrum>();
        foreach (var element in scenario.Elements)
        {
            var executing = scenario.Tests
                .Where(t => element.ExecutingTestIds.Contains(t.TestId))
                .ToList();

            spectrum.Add(new ExecutionSpectrum
            {
                ElementId = element.ElementId,
                FilePath = element.FilePath,
                FailedCount = executing.Count(t => t.Failed),
                PassedCount = executing.Count(t => !t.Failed),
                FailSkipCount = scenario.Tests.Count(t => t.Failed) - executing.Count(t => t.Failed),
                PassSkipCount = scenario.Tests.Count(t => !t.Failed) - executing.Count(t => !t.Failed)
            });
        }
        return spectrum;
    }
}