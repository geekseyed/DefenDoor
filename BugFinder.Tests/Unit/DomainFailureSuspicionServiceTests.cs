using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class DomainFailureSuspicionServiceTests
{
    private readonly DomainFailureSuspicionService _service;

    public DomainFailureSuspicionServiceTests()
    {
        _service = new DomainFailureSuspicionService();
    }

    [Fact]
    public void EnhanceWithDomainData_AppliesWeight_ToCriticalFailures()
    {
        // Arrange
        var baseSpectra = new List<ExecutionSpectrum>
        {
            new ExecutionSpectrum { ElementId = "File.cs:10", FailedCount = 1, PassedCount = 0 }
        };

        var domainFailures = new List<DomainFailureSpectrum>
        {
            new DomainFailureSpectrum
            {
                SubControlId = "EVL-CRIT",
                Severity = "Critical",
                CoveredLines = new List<string> { "File.cs:10" }
            }
        };

        // Act
        var weighted = _service.EnhanceWithDomainData(baseSpectra, domainFailures);
        _service.ApplyWeightToScores(weighted, totalFailedTests: 1);

        // Assert
        var line10 = weighted.First();
        line10.DomainFailureCount.Should().Be(1);
        line10.DomainWeightFactor.Should().BeGreaterThan(1.0); // Should be Base + Critical Bonus
        line10.AdjustedSuspiciousness.Should().Be(1.0); // Maxed out due to weight
    }

    [Fact]
    public void EnhanceWithDomainData_NoDomainFailures_KeepsBaseScore()
    {
        // Arrange
        var baseSpectra = new List<ExecutionSpectrum>
        {
            new ExecutionSpectrum { ElementId = "File.cs:20", FailedCount = 1, PassedCount = 5 }
        };

        // Act
        var weighted = _service.EnhanceWithDomainData(baseSpectra, new List<DomainFailureSpectrum>());
        _service.ApplyWeightToScores(weighted, totalFailedTests: 1);

        // Assert
        var line20 = weighted.First();
        line20.DomainFailureCount.Should().Be(0);
        line20.DomainWeightFactor.Should().Be(1.0);
        // Adjusted score should equal base Ochiai score (no boost)
    }

    [Fact]
    public void EnhanceWithDomainData_IsolationBonus_AppliesCorrectly()
    {
        // Arrange: Line executed by failing domain check, but 0 passing tests
        var baseSpectra = new List<ExecutionSpectrum>
        {
            new ExecutionSpectrum { ElementId = "File.cs:30", FailedCount = 0, PassedCount = 0 }
        };

        var domainFailures = new List<DomainFailureSpectrum>
        {
            new DomainFailureSpectrum
            {
                Severity = "High",
                CoveredLines = new List<string> { "File.cs:30" }
            }
        };

        // Act
        var weighted = _service.EnhanceWithDomainData(baseSpectra, domainFailures);

        // Assert
        var line30 = weighted.First();
        // Expect Base (1.5) + High (0.5) + Isolation (0.5) = 2.5
        line30.DomainWeightFactor.Should().Be(2.5);
    }
}