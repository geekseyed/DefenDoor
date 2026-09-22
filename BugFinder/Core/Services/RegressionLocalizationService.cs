using ISCM.BugFinder.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-10.8: Regression Localization Service
/// Orchestrates all previous services to produce a final root-cause report.
/// </summary>
public class RegressionLocalizationService
{
    private readonly RegressionHistoryService _historyService;
    private readonly RegressionCorrelationService _correlationService;
    private readonly GitDiffAnalysisService _diffService;

    public RegressionLocalizationService(string? workingDirectory = null)
    {
        _historyService = new RegressionHistoryService(workingDirectory);
        _correlationService = new RegressionCorrelationService(workingDirectory);
        _diffService = new GitDiffAnalysisService(workingDirectory);
    }

    /// <summary>
    /// BF-10.8 - Main Entry Point:
    /// Performs end-to-end regression analysis and returns a actionable report.
    /// </summary>
    public async Task<RegressionLocalizationReport> LocalizeRegressionAsync(
        List<string> failingTestNames,
        int maxCommitsToSearch = 50,
        CancellationToken cancellationToken = default)
    {
        var report = new RegressionLocalizationReport
        {
            TotalFailingTests = failingTestNames.Count
        };

        // Step 1: Find the regression boundary (Last Passing -> First Failing)
        var passingResult = await _historyService.FindLastPassingRevisionAsync(maxCommitsToSearch, cancellationToken);

        if (!passingResult.Success || string.IsNullOrEmpty(passingResult.LastPassingCommitSha))
        {
            report.Hypotheses.Add(new RootCauseHypothesis
            {
                Explanation = "Could not identify the last passing commit. History search failed.",
                ConfidenceScore = 0.0
            });
            return report;
        }

        report.LastPassingSha = passingResult.LastPassingCommitSha;

        // For simplicity in this version, we assume HEAD is the first failing commit
        // In a full implementation, we would call FindFirstFailingRevisionAsync here
        var currentInfo = new GitIntegrationService().GetCurrentRevisionInfo();
        report.FirstFailingSha = currentInfo.CurrentCommitSha ?? "HEAD";

        // Step 2: Correlate changes with failures to find suspect files
        var correlationResult = _correlationService.AnalyzeCorrelation(
            report.LastPassingSha,
            report.FirstFailingSha,
            failingTestNames);

        report.TotalCommitsAnalyzed = correlationResult.CommitsInRange.Count;
        report.TotalFilesChanged = correlationResult.SuspectFiles.Count;

        // Step 3: Deep dive into top suspects to extract specific hunks
        var topSuspects = correlationResult.SuspectFiles.Take(5).ToList(); // Top 5 suspects

        foreach (var suspect in topSuspects)
        {
            var hypothesis = new RootCauseHypothesis
            {
                SuspectFile = suspect,
                RelatedFailingTests = failingTestNames, // Simplified: assume all tests related to top suspects
                ConfidenceScore = suspect.SuspicionScore
            };

            // Get detailed diff for this specific file across the range
            var diff = _diffService.AnalyzeDiff(report.LastPassingSha, report.FirstFailingSha);

            var fileDiff = diff.Files.FirstOrDefault(f =>
                f.NewFilePath == suspect.FilePath || f.OldFilePath == suspect.FilePath);

            if (fileDiff != null)
            {
                hypothesis.SuspiciousHunks = fileDiff.Hunks;

                // Generate explanation
                var sb = new StringBuilder();
                sb.Append($"File '{suspect.FilePath}' was modified in {suspect.ChangeFrequency} commit(s). ");
                sb.Append($"It contains {fileDiff.LinesAdded} additions and {fileDiff.LinesDeleted} deletions. ");

                if (suspect.IsTestFile)
                {
                    sb.Append("Note: This is a test file; the root cause may be in the logic it tests.");
                }
                else
                {
                    sb.Append("High probability this file contains the regression source.");
                }

                hypothesis.Explanation = sb.ToString();
                hypothesis.SuggestedAction = $"Review changes in '{suspect.FilePath}', specifically around lines: {GetChangedLines(fileDiff)}";
            }
            else
            {
                hypothesis.Explanation = $"File '{suspect.FilePath}' was identified as highly changed but detailed diff extraction failed.";
                hypothesis.SuggestedAction = "Manually inspect the file history.";
            }

            report.Hypotheses.Add(hypothesis);
        }

        // Sort hypotheses by confidence
        report.Hypotheses = report.Hypotheses.OrderByDescending(h => h.ConfidenceScore).ToList();

        // Assign Ranks
        for (int i = 0; i < report.Hypotheses.Count; i++)
        {
            report.Hypotheses[i].Rank = i + 1;
        }

        return report;
    }

    private string GetChangedLines(DiffFile file)
    {
        var lines = new List<int>();
        foreach (var hunk in file.Hunks)
        {
            lines.Add(hunk.NewStartLine);
        }
        return lines.Count > 0 ? string.Join(", ", lines) : "Unknown";
    }
}