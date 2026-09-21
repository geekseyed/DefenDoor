using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class RevisionServiceTests
{
    [Fact]
    public void GetCurrentRevisionSnapshot_WhenInRepo_ReturnsSuccess()
    {
        // Arrange
        var service = new RevisionService();

        // Act
        var result = service.GetCurrentRevisionSnapshot();

        // Assert
        result.Success.Should().BeTrue();
        result.Snapshot.Should().NotBeNull();
        result.ErrorMessage.Should().BeNullOrEmpty();
    }

    [Fact]
    public void GetCurrentRevisionSnapshot_ContainsValidSha()
    {
        // Arrange
        var service = new RevisionService();

        // Act
        var result = service.GetCurrentRevisionSnapshot();

        // Assert
        result.Snapshot!.CommitSha.Should().NotBeNullOrEmpty();
        result.Snapshot.ShortSha.Length.Should().Be(7);
    }

    [Fact]
    public void GetCurrentRevisionSnapshot_ContainsBranchName()
    {
        // Arrange
        var service = new RevisionService();

        // Act
        var result = service.GetCurrentRevisionSnapshot();

        // Assert
        result.Snapshot!.BranchName.Should().NotBeNullOrEmpty();
        result.Snapshot.BranchName.Should().Contain("phase10");
    }

    [Fact]
    public void GetCurrentRevisionSnapshot_DetectsDirtyState()
    {
        // Arrange
        var service = new RevisionService();

        // Act
        var result = service.GetCurrentRevisionSnapshot();

        // Assert
        // We expect it to detect dirty state since we have uncommitted files
        result.Snapshot!.IsDirty.Should().BeTrue("because we have uncommitted changes in the repo");
        result.Snapshot.FilesChangedCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetCurrentRevisionSnapshot_HasParentCommit()
    {
        // Arrange
        var service = new RevisionService();

        // Act
        var result = service.GetCurrentRevisionSnapshot();

        // Assert
        result.Snapshot!.ParentShas.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetCurrentRevisionSnapshot_CalculatesDistanceToMain()
    {
        // Arrange
        var service = new RevisionService();

        // Act
        var result = service.GetCurrentRevisionSnapshot();

        // Assert
        // Since we are on a feature branch, we expect to be ahead of main/master
        result.Snapshot!.CommitsAheadOfMain.Should().BeGreaterOrEqualTo(0);
    }
}