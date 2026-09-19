using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class EvidenceServiceTests
{
    private readonly EvidenceService _service;

    public EvidenceServiceTests()
    {
        _service = new EvidenceService();
    }

    [Fact]
    public void CollectEvidence_WithCompleteFailure_ReturnsPackageWithAllEvidenceTypes()
    {
        // Arrange
        var failure = new Failure
        {
            Identity = new FailureIdentity { TestName = "FailingTest", ClassName = "MyTests", AssemblyName = "ISCM.Tests" },
            Type = FailureType.ExceptionFailure,
            Message = "NullReferenceException: Object reference not set to an instance of an object.",
            StackTrace = "   at ISCM.Application.Service.DoWork() in C:\\Src\\Service.cs:line 42",
            DetectedAt = DateTime.UtcNow,
            Localization = new FailureLocalization
            {
                PrimaryFilePath = "C:\\Src\\Service.cs",
                PrimaryLineNumber = 42,
                MethodName = "DoWork",
                Confidence = LocalizationConfidence.High
            },
            Metadata = new Dictionary<string, string>
            {
                { "SubControlId", "PWD-001.1" },
                { "Expected", "True" },
                { "Actual", "False" }
            }
        };

        var testResult = new NormalizedTestResult
        {
            Identity = failure.Identity,
            Outcome = TestOutcome.Failed,
            Duration = TimeSpan.FromSeconds(1.5),
            ExecutedAt = DateTime.UtcNow.AddSeconds(-2)
        };

        // Act
        var package = _service.CollectEvidence(failure, testResult);

        // Assert
        package.Should().NotBeNull();
        package.FailureId.Should().Be("ISCM.Tests:MyTests.FailingTest");
        package.Items.Count.Should().BeGreaterOrEqualTo(5); // At least 5 types of evidence

        package.Items.Should().Contain(i => i.Type == EvidenceType.TestExecution);
        package.Items.Should().Contain(i => i.Type == EvidenceType.Exception);
        package.Items.Should().Contain(i => i.Type == EvidenceType.DomainEvaluation);
        package.Items.Should().Contain(i => i.Type == EvidenceType.SourceLocation);
        package.Items.Should().Contain(i => i.Type == EvidenceType.ErrorMessage);
    }

    [Fact]
    public void CollectEvidence_WithMinimalFailure_ReturnsPackageWithBasicEvidence()
    {
        // Arrange
        var failure = new Failure
        {
            Identity = new FailureIdentity { TestName = "SimpleFail", ClassName = "Tests" },
            Type = FailureType.TestFailure,
            Message = "Assert.Equal() Failure",
            DetectedAt = DateTime.UtcNow
        };

        // Act
        var package = _service.CollectEvidence(failure);

        // Assert
        package.Should().NotBeNull();
        package.Items.Count.Should().BeGreaterOrEqualTo(1);
        package.Items.Should().Contain(i => i.Type == EvidenceType.ErrorMessage);
    }
}