using System.Diagnostics;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-10.2: Current Revision Service
/// Enriches basic commit info with complete metadata, dirty state, and relationship to main branch
/// </summary>
public class RevisionService
{
    private readonly GitIntegrationService _gitService;
    private readonly string? _repositoryRootPath;

    public RevisionService(string? workingDirectory = null)
    {
        _gitService = new GitIntegrationService(workingDirectory);
        _repositoryRootPath = _gitService.GetRepositoryRootPath();
    }

    /// <summary>
    /// BF-10.2 - Stage 1-4: Get complete revision snapshot
    /// </summary>
    public RevisionSnapshotResult GetCurrentRevisionSnapshot()
    {
        if (string.IsNullOrEmpty(_repositoryRootPath))
        {
            return new RevisionSnapshotResult
            {
                Success = false,
                ErrorMessage = "Not a Git repository"
            };
        }

        try
        {
            var snapshot = new RevisionSnapshot();

            // Basic Info (reuse GitIntegrationService)
            var basicInfo = _gitService.GetCurrentRevisionInfo();
            if (!basicInfo.IsGitRepository)
            {
                return new RevisionSnapshotResult
                {
                    Success = false,
                    ErrorMessage = "Git repository not found"
                };
            }

            snapshot.CommitSha = basicInfo.CurrentCommitSha ?? string.Empty;
            snapshot.ShortSha = snapshot.CommitSha.Length >= 7
                ? snapshot.CommitSha.Substring(0, 7)
                : snapshot.CommitSha;
            snapshot.BranchName = basicInfo.CurrentBranch ?? string.Empty;
            snapshot.Message = basicInfo.CommitMessage ?? string.Empty;
            snapshot.AuthorName = basicInfo.AuthorName ?? string.Empty;
            snapshot.CommitTimestamp = basicInfo.CommitTimestamp ?? DateTime.MinValue;

            // Enhanced Commit Metadata (FIXED: Combined arguments)
            var commitDetails = ExecuteGitCommand("show", "-s --format=%H|%h|%an|%ae|%ai|%cn|%ce|%ci|%P|%s HEAD");
            var parts = commitDetails.Split('|');
            if (parts.Length >= 10)
            {
                snapshot.AuthorEmail = parts[3];
                snapshot.CommitterTimestamp = DateTime.TryParse(parts[7], out var ct) ? ct : null;

                // Parent SHAs
                if (!string.IsNullOrEmpty(parts[8]))
                {
                    snapshot.ParentShas = parts[8].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                }
            }

            // Dirty State (uncommitted changes)
            snapshot.IsDirty = IsWorkingTreeDirty();

            // Changed Files Count
            if (snapshot.IsDirty)
            {
                var changedFiles = ExecuteGitCommand("diff", "--name-only HEAD");
                if (!string.IsNullOrWhiteSpace(changedFiles))
                {
                    snapshot.ChangedFiles = changedFiles.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
                    snapshot.FilesChangedCount = snapshot.ChangedFiles.Count;
                }
            }

            // Tags on this commit
            var tags = ExecuteGitCommand("tag", "--points-at HEAD");
            if (!string.IsNullOrWhiteSpace(tags))
            {
                snapshot.TagsOnThisCommit = tags.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            }

            // Distance to main/master
            CalculateDistanceToMain(snapshot);

            return new RevisionSnapshotResult
            {
                Success = true,
                Snapshot = snapshot
            };
        }
        catch (Exception ex)
        {
            return new RevisionSnapshotResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private bool IsWorkingTreeDirty()
    {
        try
        {
            // FIXED: Combined arguments
            var status = ExecuteGitCommand("status", "--porcelain");
            return !string.IsNullOrWhiteSpace(status);
        }
        catch
        {
            return false;
        }
    }

    private void CalculateDistanceToMain(RevisionSnapshot snapshot)
    {
        try
        {
            string mainBranch = "main";
            // FIXED: Combined arguments
            var revParse = ExecuteGitCommand("rev-parse", "--verify main");
            if (string.IsNullOrWhiteSpace(revParse))
            {
                mainBranch = "master";
                revParse = ExecuteGitCommand("rev-parse", "--verify master");
                if (string.IsNullOrWhiteSpace(revParse))
                {
                    return;
                }
            }

            // FIXED: Combined arguments
            var aheadBehind = ExecuteGitCommand("rev-list", "--left-right --count HEAD...main");
            // Retry with master if main failed implicitly or returned empty
            if (string.IsNullOrWhiteSpace(aheadBehind) && mainBranch == "main")
            {
                aheadBehind = ExecuteGitCommand("rev-list", "--left-right --count HEAD...master");
            }

            if (!string.IsNullOrWhiteSpace(aheadBehind))
            {
                var parts = aheadBehind.Split('\t');
                if (parts.Length == 2)
                {
                    if (int.TryParse(parts[0], out var ahead))
                    {
                        snapshot.CommitsAheadOfMain = ahead;
                    }
                    if (int.TryParse(parts[1], out var behind))
                    {
                        snapshot.CommitsBehindMain = behind;
                    }
                }
            }
        }
        catch
        {
            // Ignore errors in distance calculation
        }
    }

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