using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class FailureDetectionServiceTests
{
    [Fact]
    public void DetectFailures_WithTestFailure_ReturnsOneTestFailure()
    {
        // Arrange
        var service = new FailureDetectionService();
        var session = new BugFinderSession
        {
            TestExecutions = new List<NormalizedTestResult>
            {
                new NormalizedTestResult
                {
                    Identity = new FailureIdentity { TestName = "FailingTest", ClassName = "MyTests" },
                    Outcome = TestOutcome.Failed,
                    ErrorMessage = "Assert.Equal() Failure"
                }
            }
        };

        // Act
        var failures = service.DetectFailures(session);

        // Assert
        failures.Count.Should().Be(1);
        failures[0].Type.Should().Be(FailureType.TestFailure);
        failures[0].Identity.TestName.Should().Be("FailingTest");
    }

    [Fact]
    public void DetectFailures_WithDomainEvaluationFailure_ReturnsOneDomainFailure()
    {
        // Arrange
        var service = new FailureDetectionService();
        var session = new BugFinderSession
        {
            EvaluationResults = new List<NormalizedEvaluationResult>
            {
                new NormalizedEvaluationResult
                {
                    SubControlId = "PWD-001.1",
                    Status = Domain.Enums.CheckStatus.Fail,
                    Reason = "Integer 0 is less than 14."
                }
            }
        };

        // Act
        var failures = service.DetectFailures(session);

        // Assert
        failures.Count.Should().Be(1);
        failures[0].Type.Should().Be(FailureType.DomainEvaluationFailure);
        failures[0].Identity.TestName.Should().Be("PWD-001.1");
    }

    [Fact]
    public void DetectFailures_WithCompositeFailure_ReturnsTwoFailures()
    {
        // Arrange
        var service = new FailureDetectionService();
        var session = new BugFinderSession
        {
            TestExecutions = new List<NormalizedTestResult>
            {
                new NormalizedTestResult
                {
                    Identity = new FailureIdentity { TestName = "TestA", ClassName = "Tests" },
                    Outcome = TestOutcome.Passed
                }
            },
            EvaluationResults = new List<NormalizedEvaluationResult>
            {
                new NormalizedEvaluationResult
                {
                    SubControlId = "EVL-001.2",
                    Status = Domain.Enums.CheckStatus.Fail,
                    Reason = "Value mismatch"
                }
            }
        };

        // Act
        var failures = service.DetectFailures(session);

        // Assert
        failures.Count.Should().Be(1); // Only the domain failure is detected
        failures[0].Type.Should().Be(FailureType.DomainEvaluationFailure);
    }
}