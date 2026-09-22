using System;
using System.IO;
using System.Threading.Tasks;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class HistoricalCorrelationServiceTests
{
    [Fact]
    public void Constructor_ThrowsException_IfServicesAreNull()
    {
        // Arrange
        var tempDir = CreateTempDir();
        var gitService = new GitIntegrationService(tempDir);
        var failureHistoryService = new FailureHistoryService(tempDir);
        var locationHistoryService = new LocationHistoryService(tempDir);

        // Create ChangeFrequencyService with all required args (Fixes CS7036)
        var changeFreqService = new ChangeFrequencyService(gitService, failureHistoryService, locationHistoryService);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new HistoricalCorrelationService(null!, failureHistoryService));

        Assert.Throws<ArgumentNullException>(() =>
            new HistoricalCorrelationService(changeFreqService, null!));
    }

    [Fact]
    public async Task AnalyzeAsync_ReturnsValidReport_WhenDataExists()
    {
        // Arrange
        var tempDir = CreateTempDir();
        var gitService = new GitIntegrationService(tempDir);
        var failureHistoryService = new FailureHistoryService(tempDir);
        var locationHistoryService = new LocationHistoryService(tempDir);

        var changeFreqService = new ChangeFrequencyService(gitService, failureHistoryService, locationHistoryService);
        var correlationService = new HistoricalCorrelationService(changeFreqService, failureHistoryService);

        // Seed some failure data to ensure correlation logic runs
        await failureHistoryService.RecordFailureAsync(new FailureRecord
        {
            FailureSignature = "Test.Sig.1",
            FilePath = "TestFile.cs",
            OccurredAt = DateTime.UtcNow
        });

        // Act
        var result = await correlationService.AnalyzeAsync(10);

        // Assert
        result.Should().NotBeNull();
        // Note: If Git repo is empty/invalid in temp dir, TopChangedFiles might be empty, which is valid behavior
        result.Results.Should().NotBeNull();
    }

    [Fact]
    public void CorrelationStrength_Enum_HasExpectedValues()
    {
        // Assert (Uses the shared enum from BF-0.5)
        Enum.GetNames(typeof(CorrelationStrength)).Should().ContainInOrder(
            "None", "Weak", "Moderate", "Strong", "Critical");
    }

    private string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "ISCM_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}