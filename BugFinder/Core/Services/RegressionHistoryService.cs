using System.Diagnostics;
using ISCM.BugFinder.Core.Models;  


namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-10.3 & BF-10.4: Regression History Service
/// Searches git history to find last passing and first failing commits.
/// </summary>
public class RegressionHistoryService
{
    private readonly GitIntegrationService _gitService;
    private readonly string? _repositoryRootPath;
    private readonly string _originalBranch;

    public RegressionHistoryService(string? workingDirectory = null)
    {
        _gitService = new GitIntegrationService(workingDirectory);
        _repositoryRootPath = _gitService.GetRepositoryRootPath();

        var basicInfo = _gitService.GetCurrentRevisionInfo();
        _originalBranch = basicInfo.CurrentBranch ?? "HEAD";
    }

    /// <summary>
    /// BF-10.3 - Stage 1-4: Find the last commit where all tests passed
    /// Uses linear backward search from HEAD.
    /// </summary>
    public async Task<RegressionSearchResult> FindLastPassingRevisionAsync(int maxCommitsToSearch = 50, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_repositoryRootPath))
        {
            return new RegressionSearchResult
            {
                Success = false,
                ErrorMessage = "Not a Git repository"
            };
        }

        try
        {
            var result = new RegressionSearchResult { Strategy = SearchStrategy.LinearBackward };

            // Get commit history
            var currentInfo = _gitService.GetCurrentRevisionInfo();
            var commits = _gitService.GetCommitsBetween($"{currentInfo.CurrentCommitSha}~{maxCommitsToSearch}", currentInfo.CurrentCommitSha!);

            if (commits.Count == 0)
            {
                return new RegressionSearchResult
                {
                    Success = false,
                    ErrorMessage = "No commits found to search"
                };
            }

            // Search backward from HEAD (most recent first)
            foreach (var commit in commits.OrderByDescending(c => c.Timestamp))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var testResult = await ExecuteTestAtCommitAsync(commit.Sha, cancellationToken);
                result.TestedCommits.Add(testResult);
                result.CommitsSearched++;

                if (testResult.Status == TestStatus.Passed)
                {
                    result.LastPassingCommitSha = commit.Sha;
                    result.LastPassingTimestamp = commit.Timestamp;
                    result.Success = true;

                    // Restore original branch
                    await RestoreOriginalBranchAsync(cancellationToken);
                    return result;
                }
            }

            // No passing commit found in range
            result.Success = false;
            result.ErrorMessage = $"No passing commit found in last {maxCommitsToSearch} commits";

            await RestoreOriginalBranchAsync(cancellationToken);
            return result;
        }
        catch (OperationCanceledException)
        {
            await RestoreOriginalBranchAsync(cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            await RestoreOriginalBranchAsync(cancellationToken);
            return new RegressionSearchResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// BF-10.4 - Stage 1-4: Find the first commit where tests started failing
    /// Assumes we're searching between last passing and current HEAD.
    /// </summary>
    public async Task<RegressionSearchResult> FindFirstFailingRevisionAsync(string lastPassingSha, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_repositoryRootPath) || string.IsNullOrEmpty(lastPassingSha))
        {
            return new RegressionSearchResult
            {
                Success = false,
                ErrorMessage = "Invalid parameters"
            };
        }

        try
        {
            var result = new RegressionSearchResult { Strategy = SearchStrategy.LinearBackward };

            // Get commits between last passing and HEAD
            var commits = _gitService.GetCommitsBetween(lastPassingSha, "HEAD");

            if (commits.Count == 0)
            {
                return new RegressionSearchResult
                {
                    Success = false,
                    ErrorMessage = "No commits found between last passing and HEAD"
                };
            }

            // Search forward from last passing (oldest first after the passing one)
            foreach (var commit in commits.OrderBy(c => c.Timestamp))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var testResult = await ExecuteTestAtCommitAsync(commit.Sha, cancellationToken);
                result.TestedCommits.Add(testResult);
                result.CommitsSearched++;

                if (testResult.Status == TestStatus.Failed || testResult.Status == TestStatus.Error)
                {
                    result.FirstFailingCommitSha = commit.Sha;
                    result.FirstFailingTimestamp = commit.Timestamp;
                    result.Success = true;

                    await RestoreOriginalBranchAsync(cancellationToken);
                    return result;
                }
            }

            result.Success = false;
            result.ErrorMessage = "No failing commit found after last passing commit";

            await RestoreOriginalBranchAsync(cancellationToken);
            return result;
        }
        catch (OperationCanceledException)
        {
            await RestoreOriginalBranchAsync(cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            await RestoreOriginalBranchAsync(cancellationToken);
            return new RegressionSearchResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Execute tests at a specific commit (detached HEAD state)
    /// </summary>
    private async Task<CommitTestResult> ExecuteTestAtCommitAsync(string commitSha, CancellationToken cancellationToken)
    {
        var result = new CommitTestResult
        {
            CommitSha = commitSha,
            ShortSha = commitSha.Length >= 7 ? commitSha.Substring(0, 7) : commitSha
        };

        try
        {
            // Checkout commit (detached HEAD)
            CheckoutCommit(commitSha);

            // Get commit message
            var commitInfo = _gitService.GetCurrentRevisionInfo();
            result.Message = commitInfo.CommitMessage ?? string.Empty;
            result.Timestamp = commitInfo.CommitTimestamp ?? DateTime.MinValue;

            // Run tests
            var testOutput = await RunDotnetTestAsync(cancellationToken);

            // Parse results
            ParseTestOutput(testOutput, result);
        }
        catch (Exception ex)
        {
            result.Status = TestStatus.Error;
            result.FailureMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Checkout a specific commit (detached HEAD)
    /// </summary>
    private void CheckoutCommit(string commitSha)
    {
        if (string.IsNullOrEmpty(_repositoryRootPath))
        {
            throw new InvalidOperationException("Repository root not found");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"checkout --detach {commitSha}",
            WorkingDirectory = _repositoryRootPath,
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
            throw new InvalidOperationException($"Git checkout failed: {error}");
        }
    }

    /// <summary>
    /// Restore original branch
    /// </summary>
    private async Task RestoreOriginalBranchAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_repositoryRootPath) || string.IsNullOrEmpty(_originalBranch))
        {
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"checkout {_originalBranch}",
                WorkingDirectory = _repositoryRootPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            await Task.Run(() =>
            {
                process.Start();
                process.WaitForExit();
            }, cancellationToken);
        }
        catch
        {
            // Ignore restore errors
        }
    }

    /// <summary>
    /// Run dotnet test and capture output
    /// </summary>
    private async Task<string> RunDotnetTestAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_repositoryRootPath))
        {
            throw new InvalidOperationException("Repository root not found");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "test --logger \"console;verbosity=normal\"",
            WorkingDirectory = _repositoryRootPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };

        await Task.Run(() =>
        {
            process.Start();
            process.WaitForExit();
        }, cancellationToken);

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        return output + Environment.NewLine + error;
    }

    /// <summary>
    /// Parse dotnet test output to extract results
    /// </summary>
    /// <summary>
    /// Parse dotnet test output to extract results
    /// </summary>
    private void ParseTestOutput(string output, CommitTestResult result)
    {
        // Look for patterns like "Passed!  - Failed: 0, Passed: 252, Skipped: 0"
        // or "Failed!  - Failed: 5, Passed: 247, Skipped: 0"

        var lines = output.Split('\n');
        foreach (var line in lines)
        {
            if (line.Contains("Passed!") || line.Contains("Failed!"))
            {
                if (line.Contains("Passed!"))
                {
                    result.Status = TestStatus.Passed;
                }
                else if (line.Contains("Failed!"))
                {
                    result.Status = TestStatus.Failed;
                }

                // Extract numbers using temporary variables to avoid CS0206
                var failedMatch = System.Text.RegularExpressions.Regex.Match(line, @"Failed:\s*(\d+)");
                var passedMatch = System.Text.RegularExpressions.Regex.Match(line, @"Passed:\s*(\d+)");
                var skippedMatch = System.Text.RegularExpressions.Regex.Match(line, @"Skipped:\s*(\d+)");

                if (failedMatch.Success && int.TryParse(failedMatch.Groups[1].Value, out int failedCount))
                {
                    result.FailedTests = failedCount;
                }

                if (passedMatch.Success && int.TryParse(passedMatch.Groups[1].Value, out int passedCount))
                {
                    result.PassedTests = passedCount;
                }

                if (skippedMatch.Success && int.TryParse(skippedMatch.Groups[1].Value, out int skippedCount))
                {
                    result.SkippedTests = skippedCount;
                }

                result.TotalTests = result.PassedTests + result.FailedTests + result.SkippedTests;
                break;
            }
        }

        if (result.Status == TestStatus.Unknown)
        {
            result.Status = TestStatus.Error;
            result.FailureMessage = "Could not parse test output";
        }
    }
}