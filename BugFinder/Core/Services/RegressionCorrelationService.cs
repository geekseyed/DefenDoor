using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-10.7: Regression Correlation Service
/// Correlates failing tests with code changes to identify root cause files.
/// </summary>
public class RegressionCorrelationService
{
    private readonly CommitRangeAnalysisService _rangeService;
    private readonly GitDiffAnalysisService _diffService;

    public RegressionCorrelationService(string? workingDirectory = null)
    {
        _rangeService = new CommitRangeAnalysisService(workingDirectory);
        _diffService = new GitDiffAnalysisService(workingDirectory);
    }

    /// <summary>
    /// BF-10.7 - Main Entry Point:
    /// Analyzes the range between last passing and first failing commit
    /// to find files most likely responsible for test failures.
    /// </summary>
    public RegressionCorrelationResult AnalyzeCorrelation(
        string lastPassingSha,
        string firstFailingSha,
        List<string> failingTestNames)
    {
        var result = new RegressionCorrelationResult
        {
            FromSha = lastPassingSha,
            ToSha = firstFailingSha,
            FailingTestNames = failingTestNames,
            Strategy = AnalysisStrategy.Hybrid
        };

        // 1. Get all commits in the regression range
        var rangeAnalysis = _rangeService.AnalyzeRange(lastPassingSha, firstFailingSha);
        result.CommitsInRange = rangeAnalysis.Commits;

        if (rangeAnalysis.Commits.Count == 0)
        {
            return result; // No commits to analyze
        }

        // 2. Build a map of File -> Change Frequency & Related Commits
        // استفاده از کلاس FileChangeStats که در CommitRangeModels تعریف شده است
        var fileChangeMap = new Dictionary<string, FileChangeStats>();

        foreach (var commit in rangeAnalysis.Commits)
        {
            // Get detailed diff for this commit
            // نکته: ترتیب پارامترها در AnalyzeDiff مهم است (From, To)
            // ما تغییرات از والد به فرزند را می‌خواهیم
            var parentSha = $"{commit.Sha}^1";
            var diff = _diffService.AnalyzeDiff(parentSha, commit.Sha);

            foreach (var file in diff.Files)
            {
                var filePath = string.IsNullOrEmpty(file.NewFilePath) ? file.OldFilePath : file.NewFilePath;

                if (!fileChangeMap.ContainsKey(filePath))
                {
                    fileChangeMap[filePath] = new FileChangeStats
                    {
                        FilePath = filePath,
                        ChangeCount = 0,
                        LinesAdded = 0,
                        LinesDeleted = 0,
                        CommitShas = new List<string>()
                    };
                }

                var stats = fileChangeMap[filePath];
                stats.ChangeCount++;
                stats.LinesAdded += file.LinesAdded;
                stats.LinesDeleted += file.LinesDeleted;

                if (!stats.CommitShas.Contains(commit.Sha))
                {
                    stats.CommitShas.Add(commit.Sha);
                }
            }
        }

        // 3. Calculate Suspicion Score for each file
        var maxChangeCount = fileChangeMap.Values.Any() ? fileChangeMap.Values.Max(f => f.ChangeCount) : 1;
        if (maxChangeCount == 0) maxChangeCount = 1; // Avoid division by zero

        var suspectFiles = new List<SuspectFile>();

        foreach (var kvp in fileChangeMap)
        {
            var stats = kvp.Value;
            var suspect = new SuspectFile
            {
                FilePath = stats.FilePath,
                ChangeFrequency = stats.ChangeCount,
                RelatedCommits = stats.CommitShas,
                IsTestFile = stats.FilePath.Contains(".Test") || stats.FilePath.Contains("Tests"),
                FileExtension = System.IO.Path.GetExtension(stats.FilePath)
            };

            // Scoring Logic (Hybrid):
            // Base score: Frequency ratio (0.0 to 1.0)
            double frequencyScore = (double)stats.ChangeCount / maxChangeCount;

            // Bonus: If it's not a test file (changes in logic are more suspicious than changes in tests)
            double typeBonus = suspect.IsTestFile ? 0.0 : 0.2;

            // Bonus: High churn (many lines added/deleted)
            double churnBonus = (stats.LinesAdded + stats.LinesDeleted > 50) ? 0.1 : 0.0;

            suspect.SuspicionScore = Math.Min(1.0, frequencyScore + typeBonus + churnBonus);

            suspectFiles.Add(suspect);
        }

        // 4. Sort by Suspicion Score (Descending)
        result.SuspectFiles = suspectFiles.OrderByDescending(f => f.SuspicionScore).ToList();

        return result;
    }
}