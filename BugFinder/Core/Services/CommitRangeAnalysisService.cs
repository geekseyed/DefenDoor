using System.Diagnostics;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-10.5: Commit Range Analysis Service
/// Analyzes the range of commits between two revisions to identify all changed files and commit metadata.
/// </summary>
public class CommitRangeAnalysisService
{
    private readonly GitIntegrationService _gitService;
    private readonly string? _repositoryRootPath;

    public CommitRangeAnalysisService(string? workingDirectory = null)
    {
        _gitService = new GitIntegrationService(workingDirectory);
        _repositoryRootPath = _gitService.GetRepositoryRootPath();
    }

    /// <summary>
    /// BF-10.5 - Stage 1-4: Analyze commit range and generate summary
    /// </summary>
    public CommitRangeAnalysis AnalyzeRange(string fromSha, string toSha)
    {
        if (string.IsNullOrEmpty(_repositoryRootPath))
        {
            throw new InvalidOperationException("Not a Git repository");
        }

        var result = new CommitRangeAnalysis
        {
            FromSha = fromSha,
            ToSha = toSha
        };

        try
        {
            // 1. Get list of commits
            var commits = _gitService.GetCommitsBetween(fromSha, toSha);
            result.Commits = commits;
            result.TotalCommits = commits.Count;

            if (commits.Count == 0)
            {
                return result;
            }

            // 2. Calculate time span
            var dates = commits.Select(c => c.Timestamp).Where(d => d != default).OrderBy(d => d).ToList();
            if (dates.Any())
            {
                result.TimeSpan = dates.Last() - dates.First();
            }

            // 3. Aggregate all changed files
            var allFiles = new HashSet<string>();
            var fileFrequency = new Dictionary<string, int>();
            var fileStats = new Dictionary<string, FileChangeStats>();

            foreach (var commit in commits)
            {
                foreach (var file in commit.ChangedFiles)
                {
                    allFiles.Add(file);

                    // Frequency count
                    if (!fileFrequency.ContainsKey(file))
                        fileFrequency[file] = 0;
                    fileFrequency[file]++;

                    // Detailed stats per file
                    if (!fileStats.ContainsKey(file))
                    {
                        fileStats[file] = new FileChangeStats
                        {
                            FilePath = file,
                            CommitShas = new List<string>()
                        };
                    }
                    fileStats[file].ChangeCount++;
                    fileStats[file].CommitShas.Add(commit.Sha);
                }
            }

            result.AllChangedFiles = allFiles.ToList();
            result.FileChangeFrequency = fileFrequency;

            // 4. Get diff stats for lines added/deleted (BF-10.6 integration prep)
            // We execute a summary diff to get total lines
            var diffStats = GetDiffStats(fromSha, toSha);

            result.ChangeSummary = new ChangeSetSummary
            {
                TotalFilesChanged = allFiles.Count,
                TotalLinesAdded = diffStats.LinesAdded,
                TotalLinesDeleted = diffStats.LinesDeleted,
                FileStats = fileStats.Values.ToList()
            };

            return result;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to analyze commit range: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Helper: Get line change statistics using git diff --shortstat
    /// </summary>
    private (int LinesAdded, int LinesDeleted) GetDiffStats(string fromSha, string toSha)
    {
        try
        {
            // FIX CS1501: ExecuteGitCommand only takes 2 arguments (command, arguments)
            // We combine them correctly here.
            var output = ExecuteGitCommand("diff", $"--shortstat {fromSha}..{toSha}");

            if (string.IsNullOrWhiteSpace(output))
            {
                return (0, 0);
            }

            int added = 0;
            int deleted = 0;

            // Parse output like: "3 files changed, 15 insertions(+), 7 deletions(-)"
            var addedMatch = System.Text.RegularExpressions.Regex.Match(output, @"(\d+) insertion");
            var deletedMatch = System.Text.RegularExpressions.Regex.Match(output, @"(\d+) deletion");

            if (addedMatch.Success) int.TryParse(addedMatch.Groups[1].Value, out added);
            if (deletedMatch.Success) int.TryParse(deletedMatch.Groups[1].Value, out deleted);

            return (added, deleted);
        }
        catch
        {
            return (0, 0);
        }
    }

    /// <summary>
    /// Helper: Execute git command (Private implementation matching GitIntegrationService pattern)
    /// </summary>
    private string ExecuteGitCommand(string command, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"{command} {arguments}",
            WorkingDirectory = _repositoryRootPath ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        // Don't throw on error for stat commands, just return empty
        if (process.ExitCode != 0 && !string.IsNullOrEmpty(error) && !error.Contains("fatal: bad revision"))
        {
            // Log error if needed, but suppress for pipeline flow
        }

        return output.Trim();
    }
}