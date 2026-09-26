using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-15.2: Mutation Testing Integration Service
/// Stage 1 Integration: per-test kill attribution from the BF-15.1 matrix
/// Stage 2 Score Collection: per-element + overall (overall reuses
///          MutantDetectionService.Detect - single truth)
/// Stage 3 Weak Test Detection: executes mutants but kills none
/// Stage 4 Mutation Evidence: experimental report + JSON preservation
/// </summary>
public class MutationTestingIntegrationService
{
    public MutationIntegrationReport Integrate(MutationScenario? scenario, MutationKillMatrix? matrix)
    {
        var report = new MutationIntegrationReport();
        if (scenario is null || matrix is null || matrix.Entries.Count == 0)
            return report;

        report.ScenarioName = matrix.ScenarioName;
        report.Source = matrix.Source;

        // elements executed by each test
        var elementsByTest = scenario.Elements
            .SelectMany(e => e.ExecutingTestIds.Select(testId => (testId, e.ElementId)))
            .GroupBy(p => p.testId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(p => p.ElementId).ToHashSet(StringComparer.Ordinal));

        HashSet<string> ElementsOf(string testId) =>
            elementsByTest.TryGetValue(testId, out var set)
                ? set
                : new HashSet<string>(StringComparer.Ordinal);

        var allTestIds = matrix.Tests.Select(t => t.TestId)
            .Concat(elementsByTest.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        // Stage 1 — per-test kill attribution (+ executed mutant ids for Stage 3)
        var executedIdsByTest = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var testId in allTestIds)
        {
            var elements = ElementsOf(testId);
            var executed = matrix.Entries
                .Where(e => elements.Contains(e.Candidate.ElementId))
                .OrderBy(e => e.Candidate.MutantId, StringComparer.Ordinal)
                .ToList();
            executedIdsByTest[testId] = executed.Select(e => e.Candidate.MutantId).ToList();

            var killedCount = executed.Count(e => e.KilledByTestIds.Contains(testId));
            report.TestPowers.Add(new TestMutationPower
            {
                TestId = testId,
                ExecutedMutantCount = executed.Count,
                KilledMutantCount = killedCount,
                KillingRatio = executed.Count == 0 ? 0.0 : (double)killedCount / executed.Count
            });
        }

        // Stage 3 — weak tests (findings, not verdicts)
        report.WeakTests = report.TestPowers
            .Where(p => p.IsWeak)
            .Select(p => new WeakTestFinding
            {
                TestId = p.TestId,
                ExecutedMutantCount = p.ExecutedMutantCount,
                ExecutedMutantIds = executedIdsByTest[p.TestId],
                Reason = $"executes {p.ExecutedMutantCount} mutant(s) but kills none"
            })
            .OrderBy(w => w.TestId, StringComparer.Ordinal)
            .ToList();

        // Stage 2 — per-element scores
        foreach (var group in matrix.Entries
                     .GroupBy(e => e.Candidate.ElementId, StringComparer.Ordinal)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var killed = group.Count(e => e.IsKilled);
            report.ElementScores.Add(new ElementMutationScore
            {
                ElementId = group.Key,
                MutantCount = group.Count(),
                KilledCount = killed,
                MutationScore = (double)killed / group.Count()
            });
        }

        // Stage 2 — overall (reuse BF-15.1 detection as the single truth)
        var detection = MutantDetectionService.Detect(matrix);
        report.TotalMutants = detection.TotalMutants;
        report.KilledMutants = detection.KilledCount;
        report.SurvivedMutants = detection.SurvivedCount;
        report.OverallMutationScore = detection.KillRate;

        return Finalize(report);
    }

    private static MutationIntegrationReport Finalize(MutationIntegrationReport report)
    {
        report.TestPowers = report.TestPowers
            .OrderByDescending(p => p.KilledMutantCount)
            .ThenBy(p => p.TestId, StringComparer.Ordinal)
            .ToList();
        return report;
    }

    // Stage 4 — evidence preservation (same contract as BF-15.7)
    public string ToJson(MutationIntegrationReport report)
    {
        if (report is null) throw new ArgumentNullException(nameof(report));
        return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    }
}