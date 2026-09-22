using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class SpectrumBasedFaultLocalizationServiceTests
{
    private readonly SpectrumBasedFaultLocalizationService _service;

    public SpectrumBasedFaultLocalizationServiceTests()
    {
        _service = new SpectrumBasedFaultLocalizationService();
    }

    [Fact]
    public void Analyze_WithNoFailures_ReturnsWarning()
    {
        // Arrange
        var spectra = new List<ExecutionSpectrum> { CreateSpectrum("L1", 1, 0) };

        // Act
        var report = _service.Analyze(spectra, totalPassedTests: 5, totalFailedTests: 0);

        // Assert
        report.WarningMessage.Should().Contain("No failed tests");
        report.RankedLocations.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_Ochiai_RanksHighlySuspiciousLinesFirst()
    {
        // Arrange
        // Line A: Executed by 5 failed, 0 passed -> Very Suspicious
        // Line B: Executed by 1 failed, 4 passed -> Less Suspicious
        var spectra = new List<ExecutionSpectrum>
        {
            CreateSpectrum("LineA", passed: 0, failed: 5),
            CreateSpectrum("LineB", passed: 4, failed: 1)
        };

        // Act
        var report = _service.Analyze(spectra, totalPassedTests: 5, totalFailedTests: 5, algorithm: SbflAlgorithm.Ochiai);

        // Assert
        report.RankedLocations.Should().HaveCount(2);
        report.RankedLocations[0].ElementId.Should().Be("LineA");
        report.RankedLocations[0].Rank.Should().Be(1);
        report.RankedLocations[0].OchiaiScore.Should().BeGreaterThan(0.8); // High score
    }

    [Fact]
    public void Analyze_Tarantula_CalculatesCorrectly()
    {
        // Arrange
        // Line C: Executed by ALL failed (5) and NO passed (0). Ideal suspect.
        var spectra = new List<ExecutionSpectrum>
        {
            CreateSpectrum("LineC", passed: 0, failed: 5)
        };

        // Act
        var report = _service.Analyze(spectra, totalPassedTests: 5, totalFailedTests: 5, algorithm: SbflAlgorithm.Tarantula);

        // Assert
        var loc = report.RankedLocations.First();
        loc.TarantulaScore.Should().Be(1.0); // Maximum suspiciousness
    }

    [Fact]
    public void SuspiciousLocation_Model_HasRequiredProperties()
    {
        // Arrange
        var loc = new SuspiciousLocation();

        // Assert
        loc.FinalSuspiciousness.Should().BeGreaterOrEqualTo(0.0);
        loc.FinalSuspiciousness.Should().BeLessOrEqualTo(1.0);
    }

    private ExecutionSpectrum CreateSpectrum(string id, int passed, int failed)
    {
        return new ExecutionSpectrum
        {
            ElementId = id,
            FilePath = "TestFile.cs",
            MethodName = "TestMethod",
            LineNumber = 1,
            PassedCount = passed,
            FailedCount = failed,
            PassSkipCount = 0,
            FailSkipCount = 0
        };
    }
}