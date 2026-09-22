using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class CandidateRankingServiceTests
{
    private static MultiSignalRankingReport BuildSampleReport()
    {
        return new MultiSignalRankingReport
        {
            Candidates = new List<RankedFaultCandidate>
            {
                new RankedFaultCandidate { ElementId = "Top", FilePath = "A.cs", Rank = 1, UnifiedScore = 0.9, ConfidenceLevel = "Very High" },
                new RankedFaultCandidate { ElementId = "Mid", FilePath = "B.cs", Rank = 2, UnifiedScore = 0.5, ConfidenceLevel = "Medium" },
                new RankedFaultCandidate { ElementId = "Low", FilePath = "C.cs", Rank = 3, UnifiedScore = 0.1, ConfidenceLevel = "Very Low" }
            },
            TotalCandidates = 3
        };
    }

    [Fact]
    public void Build_AttachesEvidence_ToMatchingElement()
    {
        var report = BuildSampleReport();
        var evidenceMap = new Dictionary<string, List<CandidateEvidenceRef>>
        {
            ["Top"] = new List<CandidateEvidenceRef>
            {
                new CandidateEvidenceRef { Type = CandidateEvidenceType.Sbfl, Strength = 0.9, Description = "Ochiai peak" },
                new CandidateEvidenceRef { Type = CandidateEvidenceType.DomainFailure, Strength = 1.0, Description = "EVL-001.4 FAIL" }
            }
        };

        var service = new CandidateRankingService();
        var result = service.Build(report, evidenceMap);

        var top = result.Candidates.First(c => c.ElementId == "Top");
        top.Evidence.Should().HaveCount(2);
        top.DistinctSignalCount.Should().Be(2);
    }

    [Fact]
    public void Build_CandidateWithoutEvidence_HasEmptyEvidenceAndIsCounted()
    {
        var service = new CandidateRankingService();
        var result = service.Build(BuildSampleReport(), new Dictionary<string, List<CandidateEvidenceRef>>());

        result.CandidatesWithoutEvidence.Should().Be(3);
        result.CandidatesWithEvidence.Should().Be(0);
        result.Candidates.Should().OnlyContain(c => c.Evidence.Count == 0);
    }

    [Fact]
    public void Build_SetsTopCandidate_AsRankOne()
    {
        var service = new CandidateRankingService();
        var result = service.Build(BuildSampleReport(), null);

        result.TopCandidate.Should().NotBeNull();
        result.TopCandidate!.Rank.Should().Be(1);
        result.TopCandidate.ElementId.Should().Be("Top");
    }

    [Fact]
    public void Build_KeepsRankOrdering()
    {
        var service = new CandidateRankingService();
        var result = service.Build(BuildSampleReport(), null);

        result.Candidates.Select(c => c.Rank).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Build_HandlesEmptyReport_Gracefully()
    {
        var service = new CandidateRankingService();
        var result = service.Build(new MultiSignalRankingReport(), null);

        result.Candidates.Should().BeEmpty();
        result.TopCandidate.Should().BeNull();
        result.TotalCandidates.Should().Be(0);
    }
}