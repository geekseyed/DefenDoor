using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class RegressionHistoryServiceTests
{
    [Fact]
    public void Constructor_WhenNotInGitRepository_HandlesGracefully()
    {
        // Arrange & Act
        // Passing a path that is definitely not a git repo
        var service = new RegressionHistoryService("C:\\Windows\\Temp");

        // Assert - Service should instantiate without throwing
        service.Should().NotBeNull();
    }

    [Fact]
    public async Task FindLastPassingRevisionAsync_ExecutesAndReturnsResult()
    {
        // Arrange
        var service = new RegressionHistoryService();

        // Act
        // Search only last 3 commits to keep test fast
        var result = await service.FindLastPassingRevisionAsync(maxCommitsToSearch: 3);

        // Assert
        result.Should().NotBeNull();
        // We don't assert Success=true because it depends on actual test state in history
        result.CommitsSearched.Should().BeLessThanOrEqualTo(3);
    }

    [Fact]
    public async Task FindFirstFailingRevisionAsync_RequiresLastPassingSha()
    {
        // Arrange
        var service = new RegressionHistoryService();

        // Act
        var result = await service.FindFirstFailingRevisionAsync("invalid-sha-or-no-range");

        // Assert
        result.Success.Should().BeFalse("because the SHA is invalid or no commits exist after it");
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void RegressionSearchResult_Model_HasRequiredProperties()
    {
        // Arrange
        var result = new ISCM.BugFinder.Core.Models.RegressionSearchResult();

        // Assert
        result.TestedCommits.Should().NotBeNull("because it should be initialized as empty list");
        result.Strategy.Should().Be(ISCM.BugFinder.Core.Models.SearchStrategy.LinearBackward);
    }
}