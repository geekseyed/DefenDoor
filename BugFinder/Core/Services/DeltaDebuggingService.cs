using System;
using System.Collections.Generic;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-15.3: Delta Debugging Service
/// Stage 1 Failure Input Identification: caller supplies chunks + the
///          failure predicate; the service validates the original input
///          actually fails (honest NotFailing otherwise).
/// Stage 2 Input Reduction: candidate deltas = single chunks.
/// Stage 3 Failure-Preserving Reduction: backward greedy passes; a chunk
///          is dropped only when failure is still preserved without it;
///          passes repeat until one pass removes nothing (fixpoint).
/// Stage 4 Minimal Failure Input: the fixpoint equals a 1-minimal core
///          (no single removal preserves failure) - reported explicitly.
/// Termination: every pass removes >= 1 chunk or stops; removals are
/// bounded by the original chunk count.
/// Contract: the predicate must be deterministic and side-effect-free;
/// real test execution lives OUTSIDE the Core (BF-14.9).
/// </summary>
public class DeltaDebuggingService
{
    public DeltaDebuggingReport Reduce(
        string scenarioName,
        IReadOnlyList<string> chunks,
        Func<IReadOnlyList<string>, bool> preservesFailure)
    {
        if (scenarioName is null) throw new ArgumentNullException(nameof(scenarioName));
        if (chunks is null) throw new ArgumentNullException(nameof(chunks));
        if (preservesFailure is null) throw new ArgumentNullException(nameof(preservesFailure));

        var report = new DeltaDebuggingReport
        {
            ScenarioName = scenarioName,
            OriginalChunkCount = chunks.Count
        };

        // Stage 1 — empty input
        if (chunks.Count == 0)
        {
            report.Status = DeltaDebuggingStatus.Empty;
            return report;
        }

        var current = new List<string>(chunks);

        // Stage 1 — honest check: the original input must actually fail
        if (!preservesFailure(current))
        {
            report.Status = DeltaDebuggingStatus.NotFailing;
            report.RemainingChunks = current;
            report.MinimalChunkCount = current.Count;
            report.ReductionRatio = 0.0;
            report.IsOneMinimal = false;
            return report;
        }

        // Stages 2-3 — failure-preserving reduction to fixpoint
        var attempts = new List<ReductionAttempt>();
        var removed = new List<string>();
        var pass = 0;
        bool progress;

        do
        {
            pass++;
            progress = false;

            for (var i = current.Count - 1; i >= 0; i--)
            {
                var chunk = current[i];
                var candidate = new List<string>(current);
                candidate.RemoveAt(i);

                var preserved = preservesFailure(candidate);
                attempts.Add(new ReductionAttempt
                {
                    PassNumber = pass,
                    ChunkIndex = i,
                    Chunk = chunk,
                    Removed = preserved
                });

                if (preserved)
                {
                    current = candidate;
                    removed.Add(chunk);
                    progress = true;
                }
            }
        } while (progress);

        // Stage 4 — minimal failure input
        report.Status = DeltaDebuggingStatus.Reduced;
        report.RemainingChunks = current;
        report.MinimalChunkCount = current.Count;
        report.RemovedChunks = removed;
        report.Attempts = attempts;
        report.PassCount = pass;
        report.ReductionRatio = report.OriginalChunkCount == 0
            ? 0.0
            : 1.0 - ((double)current.Count / report.OriginalChunkCount);
        report.IsOneMinimal = true; // fixpoint: the final pass removed nothing

        return report;
    }
}