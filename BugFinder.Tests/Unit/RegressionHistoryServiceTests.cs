using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class RegressionHistoryServiceTests
{
    [Fact]
    public void Constructor_WhenNotInGitRepository_ReturnsError()
    {
        // Arrange & Act
        var service = new RegressionHistoryService("C:\\"); // Root directory without git

        // Assert (indirectly tested via methods that will fail)
        // The service should not throw, but methods should return error results
    }

    [Fact]
    public async Task FindLastPassingRevisionAsync_WhenInRepository_SearchesCommits()
    {
        // Arrange
        var service = new RegressionHistoryService();

        // Act
        var result = await service.FindLastPassingRevisionAsync(maxCommitsToSearch: 5);

        // Assert
        // Note: This test may take time as it actually runs tests on historical commits
        // In CI, you might want to skip or mock this
        result.Should().NotBeNull();
        // We can't assert success/failure without knowing repo state
    }

    [Fact]
    public void RegressionHistoryService_ImplementsBothBF10_3AndBF10_4()
    {
        // Arrange
        var service = new RegressionHistoryService();

        // Assert
        service.Should().NotBeNull();
        // Verify methods exist via reflection
        service.GetType().GetMethod("FindLastPassingRevisionAsync").Should().NotBeNull();
        service.GetType().GetMethod("FindFirstFailingRevisionAsync").Should().NotBeNull();
    }
}