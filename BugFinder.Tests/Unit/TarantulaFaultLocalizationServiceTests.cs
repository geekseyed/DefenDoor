using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class TarantulaFaultLocalizationServiceTests
{
    [Fact]
    public void Calculate_WithNoFailures_ReturnsWarning()
    {
        // Arrange
        var service = new TarantulaFaultLocalizationService();
        var spectra = new List<ExecutionSpectrum> { CreateSpectrum("L1", 1, 0) };

        // Act
        var result = service.Calculate(spectra, totalPassed: 5, totalFailed: 0);

        // Assert
        result.WarningMessage.Should().Contain("requires both passed and failed");
        result.RankedLocations.Should().BeEmpty();
    }

    [Fact]
    public void Calculate_PerfectSuspect_ReturnsScore1()
    {
        // Arrange
        // Line executed by ALL failed (5) and NO passed (0).
        var service = new TarantulaFaultLocalizationService();
        var spectra = new List<ExecutionSpectrum>
        {
            CreateSpectrum("PerfectLine", passed: 0, failed: 5)
        };

        // Act
        var result = service.Calculate(spectra, totalPassed: 5, totalFailed: 5);

        // Assert
        result.RankedLocations.Should().HaveCount(1);
        result.RankedLocations[0].TarantulaScore.Should().Be(1.0);
        result.RankedLocations[0].FinalSuspiciousness.Should().Be(1.0);
    }

    [Fact]
    public void Calculate_InnocentLine_ReturnsLowScore()
    {
        // Arrange
        // Line executed by ALL passed (5) and NO failed (0).
        var service = new TarantulaFaultLocalizationService();
        var spectra = new List<ExecutionSpectrum>
        {
            CreateSpectrum("InnocentLine", passed: 5, failed: 0)
        };

        // Act
        var result = service.Calculate(spectra, totalPassed: 5, totalFailed: 5);

        // Assert
        result.RankedLocations[0].TarantulaScore.Should().Be(0.0);
    }

    [Fact]
    public void Calculate_RanksCorrectly_ByScore()
    {
        // Arrange
        var service = new TarantulaFaultLocalizationService();
        var spectra = new List<ExecutionSpectrum>
        {
            CreateSpectrum("LowSus", passed: 4, failed: 1), // Mixed
            CreateSpectrum("HighSus", passed: 0, failed: 5)  // Perfect
        };

        // Act
        var result = service.Calculate(spectra, totalPassed: 5, totalFailed: 5);

        // Assert
        result.RankedLocations[0].ElementId.Should().Be("HighSus");
        result.RankedLocations[1].ElementId.Should().Be("LowSus");
    }

    private ExecutionSpectrum CreateSpectrum(string id, int passed, int failed)
    {
        return new ExecutionSpectrum
        {
            ElementId = id,
            FilePath = "Test.cs",
            LineNumber = 1,
            PassedCount = passed,
            FailedCount = failed,
            PassSkipCount = 0,
            FailSkipCount = 0
        };
    }
}