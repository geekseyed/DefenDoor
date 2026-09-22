using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ISCM.Tests.Unit.BugFinder;

public class ChangeFrequencyServiceTests
{
    [Fact]
    public void Constructor_ThrowsException_IfServicesAreNull()
    {
        // Arrange
        var tempDir = CreateTempDir();
        var gitService = new GitIntegrationService(tempDir);
        var failureService = new FailureHistoryService(tempDir);
        var locationService = new LocationHistoryService(tempDir);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new ChangeFrequencyService(null!, failureService, locationService));
        Assert.Throws<ArgumentNullException>(() =>
            new ChangeFrequencyService(gitService, null!, locationService));
        Assert.Throws<ArgumentNullException>(() =>
            new ChangeFrequencyService(gitService, failureService, null!));
    }

    [Fact]
    public async Task GenerateHotspotReportAsync_ReturnsValidStructure()
    {
        // Arrange
        var tempDir = CreateTempDir();
        // Ensure it's a valid git repo for the service to work, otherwise it returns empty summary
        InitializeGitRepo(tempDir);

        var gitService = new GitIntegrationService(tempDir);
        var failureService = new FailureHistoryService(tempDir);
        var locationService = new LocationHistoryService(tempDir);
        var changeService = new ChangeFrequencyService(gitService, failureService, locationService);

        // Act
        var report = await changeService.GenerateHotspotReportAsync(10);

        // Assert
        report.Should().NotBeNull();
        report.TopChangedFiles.Should().NotBeNull();
        report.TopFailureProneFiles.Should().NotBeNull();
        // If not a repo or no history, summary explains it
        report.Summary.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void FileChangeHistory_Model_HasRequiredProperties()
    {
        // Arrange
        var fileHist = new FileChangeHistory();

        // Assert
        fileHist.RelatedCommitShas.Should().NotBeNull();
        fileHist.IsHotspot.Should().BeFalse(); // Default 0 commits
    }

    private string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "ISCM_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private void InitializeGitRepo(string path)
    {
        // Minimal git init to prevent errors in service
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "init",
                WorkingDirectory = path,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = new System.Diagnostics.Process { StartInfo = startInfo };
            proc.Start();
            proc.WaitForExit();
        }
        catch { /* Ignore if git not found */ }
    }
}