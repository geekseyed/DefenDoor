using System.Diagnostics;
using System.IO;
using System.Linq;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class RegressionCorrelationServiceTests
{
    [Fact]
    public void Constructor_InitializesSuccessfully()
    {
        // Arrange & Act
        var service = new RegressionCorrelationService();

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void AnalyzeCorrelation_WithValidRange_ReturnsSuspectFiles()
    {
        // Arrange
        var repositoryRoot = FindRepositoryRoot();

        if (string.IsNullOrEmpty(repositoryRoot))
        {
            // اگر مخزن گیت پیدا نشد، تست با خطا مواجه شود (طبق منطق جواد)
            throw new InvalidOperationException("Test must run inside a Git repository");
        }

        var service = new RegressionCorrelationService(repositoryRoot);
        var gitService = new GitIntegrationService(repositoryRoot);

        var currentSha = ExecuteGitCommand(repositoryRoot, "rev-parse", "HEAD");
        var parentSha = ExecuteGitCommand(repositoryRoot, "rev-parse", "HEAD^1");

        if (string.IsNullOrEmpty(currentSha) || string.IsNullOrEmpty(parentSha))
        {
            // اگر کامیت والد وجود نداشت (تک کامیت)، تست معنی ندارد
            return;
        }

        // Simulate a failing test name
        var failingTests = new List<string> { "SomeTestFailed" };

        // Act
        var result = service.AnalyzeCorrelation(parentSha, currentSha, failingTests);

        // Assert
        result.Should().NotBeNull();
        result.FromSha.Should().Be(parentSha);
        result.ToSha.Should().Be(currentSha);
        result.Strategy.Should().Be(AnalysisStrategy.Hybrid);
    }

    [Fact]
    public void SuspectFile_Model_HasRequiredProperties()
    {
        // Arrange
        var suspect = new SuspectFile();

        // Assert
        suspect.RelatedCommits.Should().NotBeNull();
        suspect.SuspicionScore.Should().BeGreaterOrEqualTo(0.0);
        suspect.SuspicionScore.Should().BeLessOrEqualTo(1.0);
    }

    [Fact]
    public void RegressionCorrelationResult_Model_HasRequiredProperties()
    {
        // Arrange
        var result = new RegressionCorrelationResult();

        // Assert
        result.SuspectFiles.Should().NotBeNull();
        result.CommitsInRange.Should().NotBeNull();
        result.FailingTestNames.Should().NotBeNull();
    }

    // Helper: Find Repo Root
    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var gitDirectory = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitDirectory) || File.Exists(gitDirectory))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        return null;
    }

    // Helper: Execute Git Command
    private static string? ExecuteGitCommand(string repositoryRoot, string command, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"{command} {arguments}",
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 ? output.Trim() : null;
    }
}