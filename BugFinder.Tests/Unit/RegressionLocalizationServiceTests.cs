using ISCM.BugFinder.Core.Services;
using ISCM.BugFinder.Core.Models;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class RegressionLocalizationServiceTests
{
    [Fact]
    public void Constructor_InitializesSuccessfully()
    {
        // Arrange & Act
        var service = new RegressionLocalizationService();

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public async Task LocalizeRegressionAsync_WithValidRepo_ReturnsReport()
    {
        // Arrange
        var service = new RegressionLocalizationService();
        var failingTests = new List<string> { "DummyTestFailure" };

        // Act
        // Note: This test might take time as it runs real git/history analysis
        var report = await service.LocalizeRegressionAsync(failingTests, maxCommitsToSearch: 3);

        // Assert
        report.Should().NotBeNull();
        report.RegressionId.Should().NotBeNullOrEmpty();
        // We don't assert on Hypotheses count because it depends on actual repo state
    }

    [Fact]
    public void RegressionLocalizationReport_Model_HasRequiredProperties()
    {
        // Arrange
        var report = new RegressionLocalizationReport();

        // Assert
        report.Hypotheses.Should().NotBeNull();
        report.GeneratedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void RootCauseHypothesis_Model_HasRequiredProperties()
    {
        // Arrange
        var hypothesis = new RootCauseHypothesis();

        // Assert
        hypothesis.SuspiciousHunks.Should().NotBeNull();
        hypothesis.RelatedFailingTests.Should().NotBeNull();
        hypothesis.ConfidenceScore.Should().BeGreaterOrEqualTo(0.0);
        hypothesis.ConfidenceScore.Should().BeLessOrEqualTo(1.0);
    }
}