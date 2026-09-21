using System.Diagnostics;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-10.1: Git Integration Service
/// Detects repository, branch, commit, and working tree state using 'git' CLI.
/// No external dependencies (LibGit2Sharp) required.
/// </summary>
public class GitIntegrationService
{
    private readonly string? _repositoryRootPath;

    public GitIntegrationService(string? workingDirectory = null)
    {
        _repositoryRootPath = FindRepositoryRoot(workingDirectory ?? Directory.GetCurrentDirectory());
    }

    /// <summary>
    /// BF-10.1 - Stage 1: Repository Detection
    /// Returns true if a Git repository is found in the directory hierarchy.
    /// </summary>
    public bool IsGitRepository()
    {
        return !string.IsNullOrEmpty(_repositoryRootPath);
    }

    /// <summary>
    /// BF-10.1 - Stage 1 & 2: Get Repository Root Path
    /// </summary>
    public string? GetRepositoryRootPath()
    {
        return _repositoryRootPath;
    }

    /// <summary>
    /// BF-10.2 - Stage 1-4: Get Current Revision Info
    /// </summary>
    public GitRepositoryInfo GetCurrentRevisionInfo()
    {
        if (string.IsNullOrEmpty(_repositoryRootPath))
        {
            return new GitRepositoryInfo { IsGitRepository = false };
        }

        var info = new GitRepositoryInfo
        {
            RepositoryRootPath = _repositoryRootPath,
            IsGitRepository = true
        };

        try
        {
            // Current Branch
            info.CurrentBranch = ExecuteGitCommand("rev-parse", "--abbrev-ref HEAD");

            // Current Commit SHA
            info.CurrentCommitSha = ExecuteGitCommand("rev-parse", "HEAD");

            // Commit Metadata
            var commitDetails = ExecuteGitCommand("show", "-s --format=%H|%an|%ae|%ai|%s HEAD");
            var parts = commitDetails.Split('|');
            if (parts.Length >= 5)
            {
                info.AuthorName = parts[1];
                if (DateTime.TryParse(parts[3], out var timestamp))
                {
                    info.CommitTimestamp = timestamp;
                }
                info.CommitMessage = parts[4];
            }
        }
        catch
        {
            // Gracefully handle git command failures
        }

        return info;
    }

    /// <summary>
    /// BF-10.5 - Stage 2: Enumerate commits between two revisions
    /// </summary>
    public List<CommitInfo> GetCommitsBetween(string fromSha, string toSha)
    {
        var commits = new List<CommitInfo>();

        if (string.IsNullOrEmpty(_repositoryRootPath))
        {
            return commits;
        }

        try
        {
            // Get commit log with file changes
            var logFormat = "%H|%an|%ai|%s";
            var logOutput = ExecuteGitCommand("log", $"--pretty=format:{logFormat} {fromSha}..{toSha}");

            if (string.IsNullOrWhiteSpace(logOutput))
            {
                return commits;
            }

            var lines = logOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split('|');
                if (parts.Length >= 4)
                {
                    var sha = parts[0];
                    var commit = new CommitInfo
                    {
                        Sha = sha,
                        ShortSha = sha.Substring(0, Math.Min(7, sha.Length)),
                        AuthorName = parts[1],
                        Message = parts[3]
                    };

                    if (DateTime.TryParse(parts[2], out var timestamp))
                    {
                        commit.Timestamp = timestamp;
                    }

                    // Get changed files for this commit
                    var filesOutput = ExecuteGitCommand("diff-tree", $"--no-commit-id --name-only -r {sha}");
                    if (!string.IsNullOrWhiteSpace(filesOutput))
                    {
                        commit.ChangedFiles = filesOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
                    }

                    commits.Add(commit);
                }
            }
        }
        catch
        {
            // Handle errors gracefully
        }

        return commits;
    }

    /// <summary>
    /// BF-10.6 - Stage 1: Get diff between two commits
    /// </summary>
    public string GetDiff(string fromSha, string toSha, string? filePath = null)
    {
        if (string.IsNullOrEmpty(_repositoryRootPath))
        {
            return string.Empty;
        }

        try
        {
            var args = $"diff {fromSha}..{toSha}";
            if (!string.IsNullOrEmpty(filePath))
            {
                args += $" -- {filePath}";
            }

            var parts = args.Split(' ', 2);
            return ExecuteGitCommand(parts[0], parts.Length > 1 ? parts[1] : "");
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Helper: Find repository root by traversing up the directory tree
    /// </summary>
    private string? FindRepositoryRoot(string startPath)
    {
        var currentDir = new DirectoryInfo(startPath);

        while (currentDir != null)
        {
            if (Directory.Exists(Path.Combine(currentDir.FullName, ".git")))
            {
                return currentDir.FullName;
            }
            currentDir = currentDir.Parent;
        }

        return null;
    }

    /// <summary>
    /// Helper: Execute git command and return output
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

        if (process.ExitCode != 0 && !string.IsNullOrEmpty(error))
        {
            throw new InvalidOperationException($"Git command failed: {error}");
        }

        return output.Trim();
    }
}
