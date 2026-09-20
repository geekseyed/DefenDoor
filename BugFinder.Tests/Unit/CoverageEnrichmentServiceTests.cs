using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class CoverageEnrichmentServiceTests
{
    private readonly CoverageEnrichmentService _service;

    public CoverageEnrichmentServiceTests()
    {
        _service = new CoverageEnrichmentService();
    }

    [Fact]
    public void EnrichFailures_WithCoveredLine_AddsCoveredMetadata()
    {
        // Arrange
        var coverage = new CoverageSession
        {
            Modules = new List<ModuleCoverage>
            {
                new ModuleCoverage
                {
                    AssemblyName = "TestAssembly",
                    Classes = new List<ClassCoverage>
                    {
                        new ClassCoverage
                        {
                            ClassName = "MyService",
                            Namespace = "MyApp",
                            Lines = new List<LineCoverage>
                            {
                                new LineCoverage { LineNumber = 10, IsCovered = true, HitCount = 5 }
                            }
                        }
                    }
                }
            }
        };

        var failures = new List<Failure>
        {
            new Failure
            {
                Identity = new FailureIdentity { ClassName = "MyApp.MyService" },
                Localization = new FailureLocalization { PrimaryLineNumber = 10 },
                Metadata = new Dictionary<string, string>()
            }
        };

        // Act
        _service.EnrichFailures(failures, coverage);

        // Assert
        failures[0].Metadata["CoverageStatus"].Should().Be("Covered");
        failures[0].Metadata["CoverageInsight"].Should().Contain("Logic Error");
    }

    [Fact]
    public void EnrichFailures_WithUncoveredLine_AddsNotCoveredMetadata()
    {
        // Arrange
        var coverage = new CoverageSession
        {
            Modules = new List<ModuleCoverage>
            {
                new ModuleCoverage
                {
                    Classes = new List<ClassCoverage>
                    {
                        new ClassCoverage
                        {
                            ClassName = "MyService",
                            Namespace = "MyApp",
                            Lines = new List<LineCoverage>
                            {
                                new LineCoverage { LineNumber = 20, IsCovered = false, HitCount = 0 }
                            }
                        }
                    }
                }
            }
        };

        var failures = new List<Failure>
        {
            new Failure
            {
                Identity = new FailureIdentity { ClassName = "MyApp.MyService" },
                Localization = new FailureLocalization { PrimaryLineNumber = 20 },
                Metadata = new Dictionary<string, string>()
            }
        };

        // Act
        _service.EnrichFailures(failures, coverage);

        // Assert
        failures[0].Metadata["CoverageStatus"].Should().Be("NotCovered");
        failures[0].Metadata["CoverageInsight"].Should().Contain("Missing Test Scenario");
    }

    [Fact]
    public void EnrichFailures_WithNoMatchingClass_AddsUnknownMetadata()
    {
        // Arrange
        var coverage = new CoverageSession
        {
            Modules = new List<ModuleCoverage>
            {
                new ModuleCoverage
                {
                    Classes = new List<ClassCoverage>
                    {
                        new ClassCoverage
                        {
                            ClassName = "OtherService",
                            Namespace = "MyApp",
                            Lines = new List<LineCoverage>()
                        }
                    }
                }
            }
        };

        var failures = new List<Failure>
        {
            new Failure
            {
                Identity = new FailureIdentity { ClassName = "MyApp.UnknownService" },
                Localization = new FailureLocalization { PrimaryLineNumber = 10 },
                Metadata = new Dictionary<string, string>()
            }
        };

        // Act
        _service.EnrichFailures(failures, coverage);

        // Assert
        failures[0].Metadata["CoverageStatus"].Should().Be("Unknown");
    }
}