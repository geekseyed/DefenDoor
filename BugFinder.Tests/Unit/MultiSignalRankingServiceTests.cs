using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class MultiSignalRankingServiceTests
{
    [Fact]
    public void Rank_WithEqualWeights_CalculatesCorrectAverage()
    {
        // Arrange
        var config = new SignalWeightConfig { SbflWeight = 1.0, AggregationWeight = 1.0, DomainWeight = 1.0 };
        var service = new MultiSignalRankingService(config);

        var inputs = new List<SignalInput>
        {
            new SignalInput
            {
                ElementId = "LineA",
                SbflScore = 1.0,
                AggregatedScore = 1.0,
                DomainFailureScore = 1.0
            }
        };

        // Act
        var result = service.Rank(inputs);

        // Assert
        result.Candidates.Should().HaveCount(1);
        result.Candidates[0].UnifiedScore.Should().BeApproximately(1.0, 0.001); // Normalized weights sum to 1, so avg of 1s is 1
    }

    [Fact]
    public void Rank_DomainFailureBoostsScore_WhenWeighted()
    {
        // Arrange
        // Give high weight to Domain to see the boost effect
        var config = new SignalWeightConfig { SbflWeight = 0.1, AggregationWeight = 0.1, DomainWeight = 0.8 };
        var service = new MultiSignalRankingService(config);

        var inputs = new List<SignalInput>
        {
            // Candidate X: Low SBFL, High Domain
            new SignalInput { ElementId = "X", SbflScore = 0.2, AggregatedScore = 0.2, DomainFailureScore = 1.0 },
            // Candidate Y: High SBFL, No Domain
            new SignalInput { ElementId = "Y", SbflScore = 0.9, AggregatedScore = 0.9, DomainFailureScore = 0.0 }
        };

        // Act
        var result = service.Rank(inputs);

        // Assert
        // X should be ranked higher or close to Y because Domain weight is dominant (0.8)
        // Score X ≈ (0.2*0.1) + (0.2*0.1) + (1.0*0.8) = 0.02 + 0.02 + 0.8 = 0.84
        // Score Y ≈ (0.9*0.1) + (0.9*0.1) + 0 = 0.09 + 0.09 = 0.18
        result.Candidates[0].ElementId.Should().Be("X");
        result.Candidates[0].UnifiedScore.Should().BeGreaterThan(0.8);
    }

    [Fact]
    public void Rank_HandlesEmptyInput_Gracefully()
    {
        // Arrange
        var service = new MultiSignalRankingService();
        var inputs = new List<SignalInput>();

        // Act
        var result = service.Rank(inputs);

        // Assert
        result.Candidates.Should().BeEmpty();
    }

    [Fact]
    public void Rank_AssignsConfidenceLevels_Correctly()
    {
        // Arrange
        var service = new MultiSignalRankingService();
        var inputs = new List<SignalInput>
        {
            new SignalInput { ElementId = "High", SbflScore = 0.9, AggregatedScore = 0.9, DomainFailureScore = 0.9 },
            new SignalInput { ElementId = "Low", SbflScore = 0.1, AggregatedScore = 0.1, DomainFailureScore = 0.1 }
        };

        // Act
        var result = service.Rank(inputs);

        // Assert
        result.Candidates.First(c => c.ElementId == "High").ConfidenceLevel.Should().Be("Very High");
        result.Candidates.First(c => c.ElementId == "Low").ConfidenceLevel.Should().Be("Very Low");
    }
}