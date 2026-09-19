using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class CorrelationServiceTests
{
    private readonly CorrelationService _service;

    public CorrelationServiceTests()
    {
        _service = new CorrelationService();
    }

    [Fact]
    public void CorrelateFailures_WithSharedMethod_CreatesStrongCluster()
    {
        // Arrange
        var failures = new List<Failure>
        {
            new Failure
            {
                Identity = new FailureIdentity { TestName = "TestA" },
                Localization = new FailureLocalization
                {
                    MethodName = "ProcessData",
                    PrimaryFilePath = "C:\\Src\\Processor.cs"
                }
            },
            new Failure
            {
                Identity = new FailureIdentity { TestName = "TestB" },
                Localization = new FailureLocalization
                {
                    MethodName = "ProcessData",
                    PrimaryFilePath = "C:\\Src\\Other.cs"
                }
            }
        };

        // Act
        var result = _service.CorrelateFailures(failures);

        // Assert
        result.Clusters.Count.Should().Be(1);
        result.Clusters[0].CorrelationStrength.Should().Be(CorrelationStrength.Strong);
        result.Clusters[0].SharedElement.ElementType.Should().Be("Method");
        result.Clusters[0].SharedElement.Name.Should().Be("ProcessData");
        result.Clusters[0].RelatedFailureIds.Count.Should().Be(2);
        result.IsolatedFailures.Count.Should().Be(0);
    }

    [Fact]
    public void CorrelateFailures_WithSharedFile_CreatesMediumCluster()
    {
        // Arrange
        var failures = new List<Failure>
        {
            new Failure
            {
                Identity = new FailureIdentity { TestName = "TestC" },
                Localization = new FailureLocalization
                {
                    MethodName = "MethodX",
                    PrimaryFilePath = "C:\\Src\\Common.cs"
                }
            },
            new Failure
            {
                Identity = new FailureIdentity { TestName = "TestD" },
                Localization = new FailureLocalization
                {
                    MethodName = "MethodY",
                    PrimaryFilePath = "C:\\Src\\Common.cs"
                }
            }
        };

        // Act
        var result = _service.CorrelateFailures(failures);

        // Assert
        result.Clusters.Count.Should().Be(1);
        result.Clusters[0].CorrelationStrength.Should().Be(CorrelationStrength.Medium);
        result.Clusters[0].SharedElement.ElementType.Should().Be("File");
        result.IsolatedFailures.Count.Should().Be(0);
    }

    [Fact]
    public void CorrelateFailures_WithNoCommonality_ReturnsIsolatedFailures()
    {
        // Arrange
        var failures = new List<Failure>
        {
            new Failure
            {
                Identity = new FailureIdentity { TestName = "TestE" },
                Localization = new FailureLocalization
                {
                    MethodName = "UniqueMethod1",
                    PrimaryFilePath = "C:\\Src\\File1.cs"
                }
            },
            new Failure
            {
                Identity = new FailureIdentity { TestName = "TestF" },
                Localization = new FailureLocalization
                {
                    MethodName = "UniqueMethod2",
                    PrimaryFilePath = "C:\\Src\\File2.cs"
                }
            }
        };

        // Act
        var result = _service.CorrelateFailures(failures);

        // Assert
        result.Clusters.Count.Should().Be(0);
        result.IsolatedFailures.Count.Should().Be(2);
    }
}