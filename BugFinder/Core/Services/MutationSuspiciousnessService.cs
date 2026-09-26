using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-15.1 Stage 5: Mutation-Based Suspiciousness (Metallaxis-style).
/// Per mutant: Ochiai over the KILL vector (m_f / sqrt(TF*(m_f+m_p)));
/// per element: MAX over its mutants. Deterministic ordering identical
/// to BF-15.7 (score desc, ElementId asc). RESEARCH ONLY - never gates
/// releases; production ranking remains BF-12.
/// </summary>
public class MutationSuspiciousnessService
{
    public const string AlgorithmNameValue = "Metallaxis(simulated)";
    public const string ResearchBoundaryNote =
        "BF-15.1 research output: mutation-based suspiciousness from SIMULATED kill data. " +
        "Read-only Core - mutants are plans, never applied.";

    public ExperimentalLocalizationResult Compute(MutationKillMatrix? matrix)
    {
        var result = new ExperimentalLocalizationResult
        {
            AlgorithmName = AlgorithmNameValue
        };
        if (matrix is null || matrix.Entries.Count == 0) return result;

        var totalFailed = matrix.Tests.Count(t => t.Failed);

        var entries = new List<ExperimentalSuspiciousness>();
        foreach (var group in matrix.Entries
                     .GroupBy(e => e.Candidate.ElementId, StringComparer.Ordinal)
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var score = 0.0;
            foreach (var mutant in group)
            {
                var mFailed = matrix.Tests
                    .Count(t => t.Failed && mutant.KilledByTestIds.Contains(t.TestId));
                var mPassed = mutant.KilledByTestIds.Count - mFailed;

                if (totalFailed > 0 && mFailed + mPassed > 0)
                {
                    var mutantScore = mFailed / Math.Sqrt((double)totalFailed * (mFailed + mPassed));
                    if (mutantScore > score) score = mutantScore;
                }
            }

            entries.Add(new ExperimentalSuspiciousness
            {
                ElementId = group.Key,
                FilePath = group.First().Candidate.FilePath,
                Score = score
            });
        }

        result.TotalElements = entries.Count;
        result.Entries = entries
            .OrderByDescending(e => e.Score)
            .ThenBy(e => e.ElementId, StringComparer.Ordinal)
            .ToList();
        for (var i = 0; i < result.Entries.Count; i++) result.Entries[i].Rank = i + 1;
        return result;
    }
}