using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class GitIntegrationServiceTests
{
    [Fact]
    public void IsGitRepository_WhenInRepository_ReturnsTrue()
    {
        // Arrange
        var service = new GitIntegrationService();

        // Act
        var isRepo = service.IsGitRepository();

        // Assert
        isRepo.Should().BeTrue("because we are running from within a git repository");
    }

    [Fact]
    public void GetCurrentRevisionInfo_ReturnsValidInfo()
    {
        // Arrange
        var service = new GitIntegrationService();

        // Act
        var info = service.GetCurrentRevisionInfo();

        // Assert
        info.IsGitRepository.Should().BeTrue();
        info.RepositoryRootPath.Should().NotBeNullOrEmpty();
        info.CurrentBranch.Should().NotBeNullOrEmpty();
        info.CurrentCommitSha.Should().NotBeNullOrEmpty();
        info.CommitTimestamp.Should().HaveValue();
    }

    [Fact]
    public void GetCommitsBetween_ReturnsCommits()
    {
        // Arrange
        var service = new GitIntegrationService();
        var currentInfo = service.GetCurrentRevisionInfo();

        // Get parent commit (assuming we have history)
        var parentSha = ExecuteGitCommand("rev-parse", $"{currentInfo.CurrentCommitSha}^1");

        if (string.IsNullOrEmpty(parentSha))
        {
            // Skip if no parent commit exists
            return;
        }

        // Act
        var commits = service.GetCommitsBetween(parentSha, currentInfo.CurrentCommitSha!);

        // Assert
        commits.Should().NotBeEmpty();
        commits[0].Sha.Should().Be(currentInfo.CurrentCommitSha);
        commits[0].ChangedFiles.Should().NotBeNullOrEmpty();
    }

    private string ExecuteGitCommand(string command, string arguments)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"{command} {arguments}",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new System.Diagnostics.Process { StartInfo = startInfo };
        process.Start();
        return process.StandardOutput.ReadToEnd().Trim();
    }
}