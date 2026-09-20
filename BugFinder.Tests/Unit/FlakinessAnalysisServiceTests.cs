using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class FlakinessAnalysisServiceTests
{
    private readonly FlakinessAnalysisService _service;

    public FlakinessAnalysisServiceTests()
    {
        _service = new FlakinessAnalysisService();
    }

    [Fact]
    public void RecordExecution_WithAlternatingOutcomes_DetectsFlakyTest()
    {
        // Arrange
        var testId = "MyTests.FlakyTest";

        // 8 alternating outcomes to ensure high flakiness score
        var outcomes = new[]
        {
            TestOutcome.Passed, TestOutcome.Failed,
            TestOutcome.Passed, TestOutcome.Failed,
            TestOutcome.Passed, TestOutcome.Failed,
            TestOutcome.Passed, TestOutcome.Failed
        };

        foreach (var outcome in outcomes)
        {
            _service.RecordExecution(testId, outcome, null, 100);
        }

        // Act
        var profile = _service.GetProfile(testId);

        // Assert
        profile.Should().NotBeNull();
        profile!.Category.Should().Be(FlakinessCategory.HighlyFlaky);
        profile.Score.Value.Should().Be(1.0); // With new formula, 8 alternating runs = 1.0
        profile.Flips.Should().Be(7);
    }

    [Fact]
    public void RecordExecution_WithConsistentPasses_DetectsStableTest()
    {
        // Arrange
        var testId = "MyTests.StableTest";
        for (int i = 0; i < 10; i++)
        {
            _service.RecordExecution(testId, TestOutcome.Passed, null, 100);
        }

        // Act
        var profile = _service.GetProfile(testId);

        // Assert
        profile.Should().NotBeNull();
        profile!.Category.Should().Be(FlakinessCategory.Stable);
        profile.Score.Value.Should().Be(0.0);
        profile.Flips.Should().Be(0);
    }

    [Fact]
    public void GetSummary_ReturnsCorrectCounts()
    {
        // Arrange
        _service.RecordExecution("StableTest", TestOutcome.Passed, null, 100);
        _service.RecordExecution("StableTest", TestOutcome.Passed, null, 100);

        _service.RecordExecution("FlakyTest", TestOutcome.Passed, null, 100);
        _service.RecordExecution("FlakyTest", TestOutcome.Failed, null, 100);
        _service.RecordExecution("FlakyTest", TestOutcome.Passed, null, 100);
        _service.RecordExecution("FlakyTest", TestOutcome.Failed, null, 100);
        _service.RecordExecution("FlakyTest", TestOutcome.Passed, null, 100);
        _service.RecordExecution("FlakyTest", TestOutcome.Failed, null, 100);
        _service.RecordExecution("FlakyTest", TestOutcome.Passed, null, 100);
        _service.RecordExecution("FlakyTest", TestOutcome.Failed, null, 100);

        // Act
        var summary = _service.GetSummary();

        // Assert
        summary.TotalTestsTracked.Should().Be(2);
        summary.StableTests.Should().Be(1);
        summary.FlakyTests.Should().Be(1);
    }
}