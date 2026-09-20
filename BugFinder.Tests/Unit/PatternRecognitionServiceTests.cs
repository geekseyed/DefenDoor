using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class PatternRecognitionServiceTests
{
    private readonly PatternRecognitionService _service;

    public PatternRecognitionServiceTests()
    {
        _service = new PatternRecognitionService();
    }

    [Fact]
    public void IdentifyPatterns_WithSimilarErrors_GroupsIntoPattern()
    {
        // Arrange
        var failures = new List<Failure>
        {
            new Failure
            {
                Identity = new FailureIdentity { TestName = "TestA" },
                Message = "Timeout waiting for response after 30 seconds",
                DetectedAt = DateTime.UtcNow.AddHours(-2)
            },
            new Failure
            {
                Identity = new FailureIdentity { TestName = "TestB" },
                Message = "Timeout waiting for response after 45 seconds",
                DetectedAt = DateTime.UtcNow.AddHours(-1)
            },
            new Failure
            {
                Identity = new FailureIdentity { TestName = "TestC" },
                Message = "Timeout waiting for response after 10 seconds",
                DetectedAt = DateTime.UtcNow
            }
        };

        // Act
        var patterns = _service.IdentifyPatterns(failures);

        // Assert
        patterns.Count.Should().Be(1);
        patterns[0].OccurrenceCount.Should().Be(3);
        patterns[0].RelatedTestIds.Count.Should().Be(3);
        patterns[0].SuggestedFix.Should().Contain("timeout");
    }

    [Fact]
    public void IdentifyPatterns_WithUniqueErrors_ReturnsEmptyList()
    {
        // Arrange
        var failures = new List<Failure>
        {
            new Failure { Message = "Error A", DetectedAt = DateTime.UtcNow },
            new Failure { Message = "Error B", DetectedAt = DateTime.UtcNow.AddHours(-1) }
        };

        // Act
        var patterns = _service.IdentifyPatterns(failures);

        // Assert
        patterns.Count.Should().Be(0); // No pattern with < 2 occurrences
    }

    [Fact]
    public void IdentifyPatterns_WithNullMessage_IgnoresFailure()
    {
        // Arrange
        var failures = new List<Failure>
        {
            new Failure { Message = null, DetectedAt = DateTime.UtcNow },
            new Failure { Message = "Valid Error", DetectedAt = DateTime.UtcNow },
            new Failure { Message = "Valid Error", DetectedAt = DateTime.UtcNow.AddHours(-1) }
        };

        // Act
        var patterns = _service.IdentifyPatterns(failures);

        // Assert
        patterns.Count.Should().Be(1); // Only the valid error forms a pattern
    }
}