using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class MultiTestAggregationServiceTests
{
    private readonly MultiTestAggregationService _aggregator;

    public MultiTestAggregationServiceTests()
    {
        _aggregator = new MultiTestAggregationService();
    }

    [Fact]
    public void Aggregate_WithMaxStrategy_PicksHighestScore()
    {
        // Arrange
        // Element A: Score 0.2 in Report1, Score 0.9 in Report2 -> Max should be 0.9
        var reports = new List<FaultLocalizationReport>
        {
            CreateReport(new SuspiciousLocation { ElementId = "A", FinalSuspiciousness = 0.2 }),
            CreateReport(new SuspiciousLocation { ElementId = "A", FinalSuspiciousness = 0.9 })
        };

        // Act
        var result = _aggregator.Aggregate(reports, AggregationStrategy.Max);

        // Assert
        result.RankedResults.Should().HaveCount(1);
        result.RankedResults[0].FinalScore.Should().Be(0.9);
        result.RankedResults[0].ContributingFailureCount.Should().Be(2);
    }

    [Fact]
    public void Aggregate_WithAverageStrategy_CalculatesMean()
    {
        // Arrange
        // Element B: Score 0.5 in Report1, Score 0.5 in Report2 -> Avg should be 0.5
        var reports = new List<FaultLocalizationReport>
        {
            CreateReport(new SuspiciousLocation { ElementId = "B", FinalSuspiciousness = 0.5 }),
            CreateReport(new SuspiciousLocation { ElementId = "B", FinalSuspiciousness = 0.5 })
        };

        // Act
        var result = _aggregator.Aggregate(reports, AggregationStrategy.Average);

        // Assert
        result.RankedResults[0].FinalScore.Should().Be(0.5);
    }

    [Fact]
    public void Aggregate_MultipleElements_RanksCorrectly()
    {
        // Arrange
        var reports = new List<FaultLocalizationReport>
        {
            CreateReport(
                new SuspiciousLocation { ElementId = "High", FinalSuspiciousness = 0.8 },
                new SuspiciousLocation { ElementId = "Low", FinalSuspiciousness = 0.1 }
            ),
            CreateReport(
                new SuspiciousLocation { ElementId = "High", FinalSuspiciousness = 0.7 },
                new SuspiciousLocation { ElementId = "Low", FinalSuspiciousness = 0.2 }
            )
        };

        // Act (Using Max strategy)
        var result = _aggregator.Aggregate(reports, AggregationStrategy.Max);

        // Assert
        result.RankedResults.Should().HaveCount(2);
        result.RankedResults[0].ElementId.Should().Be("High");
        result.RankedResults[0].FinalScore.Should().Be(0.8);
        result.RankedResults[1].ElementId.Should().Be("Low");
        result.RankedResults[1].FinalScore.Should().Be(0.2);
    }

    [Fact]
    public void Aggregate_EmptyInput_ReturnsEmptyReport()
    {
        // Arrange
        var reports = new List<FaultLocalizationReport>();

        // Act
        var result = _aggregator.Aggregate(reports);

        // Assert
        result.RankedResults.Should().BeEmpty();
        result.TotalFailuresAnalyzed.Should().Be(0);
    }

    private FaultLocalizationReport CreateReport(params SuspiciousLocation[] locations)
    {
        return new FaultLocalizationReport
        {
            RankedLocations = locations.ToList(),
            PrimaryAlgorithm = SbflAlgorithm.Ochiai
        };
    }
}