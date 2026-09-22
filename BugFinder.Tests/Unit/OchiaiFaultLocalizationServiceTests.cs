using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class OchiaiFaultLocalizationServiceTests
{
    private readonly OchiaiFaultLocalizationService _service;

    public OchiaiFaultLocalizationServiceTests()
    {
        _service = new OchiaiFaultLocalizationService();
    }

    [Fact]
    public void Analyze_ReturnsWarning_WhenNoFailuresExist()
    {
        // Arrange
        var spectra = new List<ExecutionSpectrum> { CreateSpectrum("L1", 1, 0) };

        // Act
        var report = _service.Analyze(spectra, totalPassedTests: 5, totalFailedTests: 0);

        // Assert
        report.WarningMessage.Should().Contain("requires at least one failed test");
        report.RankedResults.Should().BeEmpty();
    }

    [Fact]
    public void Analyze_PerfectSuspect_GetsScoreOfOne()
    {
        // Arrange
        // Line X: Executed by ALL 5 failed tests, and NO passed tests.
        // Formula: 5 / sqrt(5 * (5+0)) = 5 / 5 = 1.0
        var spectra = new List<ExecutionSpectrum>
        {
            CreateSpectrum("PerfectLine", passed: 0, failed: 5)
        };

        // Act
        var report = _service.Analyze(spectra, totalPassedTests: 5, totalFailedTests: 5);

        // Assert
        var result = report.TopSuspect;
        result.Should().NotBeNull();
        result!.OchiaiScore.Should().Be(1.0);
        result.Rank.Should().Be(1);
    }

    [Fact]
    public void Analyze_InnocentLine_GetsLowScore()
    {
        // Arrange
        // Line Y: Executed by 0 failed, 5 passed.
        // Formula: 0 / ... = 0.0
        var spectra = new List<ExecutionSpectrum>
        {
            CreateSpectrum("InnocentLine", passed: 5, failed: 0)
        };

        // Act
        var report = _service.Analyze(spectra, totalPassedTests: 5, totalFailedTests: 5);

        // Assert
        var result = report.RankedResults.First(r => r.ElementId == "InnocentLine");
        result.OchiaiScore.Should().Be(0.0);
    }

    [Fact]
    public void Analyze_RankingOrder_IsCorrect()
    {
        // Arrange
        var spectra = new List<ExecutionSpectrum>
        {
            CreateSpectrum("Medium", passed: 2, failed: 2), // Moderate score
            CreateSpectrum("High", passed: 0, failed: 5),   // High score (1.0)
            CreateSpectrum("Low", passed: 5, failed: 1)     // Low score
        };

        // Act
        var report = _service.Analyze(spectra, totalPassedTests: 10, totalFailedTests: 5);

        // Assert
        report.RankedResults[0].ElementId.Should().Be("High");
        report.RankedResults[0].Rank.Should().Be(1);

        report.RankedResults[1].ElementId.Should().Be("Medium");
        report.RankedResults[2].ElementId.Should().Be("Low");
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