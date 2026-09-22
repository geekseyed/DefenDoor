using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class JaccardFaultLocalizationServiceTests
{
    private readonly JaccardFaultLocalizationService _service;

    public JaccardFaultLocalizationServiceTests()
    {
        _service = new JaccardFaultLocalizationService();
    }

    [Fact]
    public void Calculate_ReturnsEmpty_WhenNoFailures()
    {
        // Arrange
        var spectra = new List<ExecutionSpectrum> { new ExecutionSpectrum { FailedCount = 0, PassedCount = 5 } };

        // Act
        var report = _service.Calculate(spectra, totalFailedTests: 0);

        // Assert
        report.RankedResults.Should().BeEmpty();
    }

    [Fact]
    public void Calculate_RanksPerfectSuspect_Highest()
    {
        // Arrange
        // Line A: Executed by 5 fails, 0 passes (Score = 5 / (0+5) = 1.0)
        // Line B: Executed by 5 fails, 5 passes (Score = 5 / (5+5) = 0.5)
        var spectra = new List<ExecutionSpectrum>
        {
            new ExecutionSpectrum { ElementId = "LineA", FailedCount = 5, PassedCount = 0 },
            new ExecutionSpectrum { ElementId = "LineB", FailedCount = 5, PassedCount = 5 }
        };

        // Act
        var report = _service.Calculate(spectra, totalFailedTests: 5);

        // Assert
        report.RankedResults.Should().HaveCount(2);
        report.RankedResults[0].ElementId.Should().Be("LineA");
        report.RankedResults[0].JaccardScore.Should().Be(1.0);
        report.RankedResults[1].JaccardScore.Should().Be(0.5);
    }

    [Fact]
    public void Calculate_AppliesTopN_Limit()
    {
        // Arrange
        var spectra = new List<ExecutionSpectrum>
        {
            new ExecutionSpectrum { ElementId = "L1", FailedCount = 1, PassedCount = 0 },
            new ExecutionSpectrum { ElementId = "L2", FailedCount = 1, PassedCount = 0 },
            new ExecutionSpectrum { ElementId = "L3", FailedCount = 1, PassedCount = 0 }
        };
        var config = new JaccardConfig { TopNResults = 2 };

        // Act
        var report = _service.Calculate(spectra, totalFailedTests: 1, config: config);

        // Assert
        report.RankedResults.Should().HaveCount(2);
    }
}