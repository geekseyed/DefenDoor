using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class CommitRangeAnalysisServiceTests
{
    [Fact]
    public void Constructor_InitializesSuccessfully()
    {
        // Arrange & Act
        var service = new CommitRangeAnalysisService();

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void AnalyzeRange_BetweenRevisions_ReturnsValidResult()
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();

        repositoryRoot.Should().NotBeNullOrEmpty(
            "the test must run inside the ISCM Git repository");

        var service = new CommitRangeAnalysisService(repositoryRoot!);

        var currentSha = ExecuteGitCommand(
            repositoryRoot!,
            "rev-parse",
            "HEAD");

        currentSha.Should().NotBeNullOrWhiteSpace(
            "HEAD must resolve to a valid Git commit");

        var parentSha = ExecuteGitCommand(
            repositoryRoot!,
            "rev-parse",
            $"{currentSha}^1");

        parentSha.Should().NotBeNullOrWhiteSpace(
            "the current commit must have a parent commit");

        // Act
        var result = service.AnalyzeRange(
            parentSha!,
            currentSha!);

        // Assert
        result.Should().NotBeNull();
        result.FromSha.Should().Be(parentSha);
        result.ToSha.Should().Be(currentSha);
        result.TotalCommits.Should().BeGreaterThan(0);
        result.Commits.Should().NotBeEmpty();
    }

    [Fact]
    public void AnalyzeRange_WithInvalidRepo_ThrowsException()
    {
        // Arrange
        var service = new CommitRangeAnalysisService(
            @"C:\Windows\Temp");

        // Act
        Action act = () =>
            service.AnalyzeRange("abc123", "def456");

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CommitRangeAnalysis_Model_HasRequiredProperties()
    {
        // Arrange
        var result = new CommitRangeAnalysis();

        // Assert
        result.Commits.Should().NotBeNull();
        result.AllChangedFiles.Should().NotBeNull();
        result.FileChangeFrequency.Should().NotBeNull();
    }

    [Fact]
    public void ChangeSetSummary_Model_HasRequiredProperties()
    {
        // Arrange
        var summary = new ChangeSetSummary();

        // Assert
        summary.FileStats.Should().NotBeNull();
    }

    [Fact]
    public void FileChangeStats_Model_HasRequiredProperties()
    {
        // Arrange
        var stats = new FileChangeStats();

        // Assert
        stats.CommitShas.Should().NotBeNull();
    }

    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            AppContext.BaseDirectory);

        while (directory != null)
        {
            var gitDirectory = Path.Combine(
                directory.FullName,
                ".git");

            if (Directory.Exists(gitDirectory) ||
                File.Exists(gitDirectory))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static string? ExecuteGitCommand(
        string repositoryRoot,
        string command,
        string arguments)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"{command} {arguments}",
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new System.Diagnostics.Process
        {
            StartInfo = startInfo
        };

        process.Start();

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        process.ExitCode.Should().Be(
            0,
            $"Git command failed: git {command} {arguments}{Environment.NewLine}" +
            $"Repository: {repositoryRoot}{Environment.NewLine}" +
            $"Git error: {error}");

        return output.Trim();
    }
}