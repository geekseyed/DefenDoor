using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-12.10: Candidate Ranking Service
/// Enriches the 12.9 multi-signal ranking with evidence association and
/// produces the final Ranked Localization Result (official BF-12 output).
/// </summary>
public class CandidateRankingService
{
    /// <summary>
    /// Attaches evidence to ranked candidates (keyed by ElementId) and builds
    /// the final localization result with summary statistics.
    /// </summary>
    public RankedLocalizationResult Build(
        MultiSignalRankingReport? rankingReport,
        Dictionary<string, List<CandidateEvidenceRef>>? evidenceMap = null)
    {
        var result = new RankedLocalizationResult();

        if (rankingReport is null || rankingReport.Candidates.Count == 0)
        {
            return result;
        }

        evidenceMap ??= new Dictionary<string, List<CandidateEvidenceRef>>();

        foreach (var ranked in rankingReport.Candidates)
        {
            var evidence = evidenceMap.TryGetValue(ranked.ElementId, out var list)
                ? list
                : new List<CandidateEvidenceRef>();

            result.Candidates.Add(new EvidenceRankedCandidate
            {
                ElementId = ranked.ElementId,
                FilePath = ranked.FilePath,
                LineNumber = ranked.LineNumber,
                UnifiedScore = ranked.UnifiedScore,
                Rank = ranked.Rank,
                ConfidenceLevel = ranked.ConfidenceLevel,
                Evidence = evidence,
                DistinctSignalCount = evidence.Select(e => e.Type).Distinct().Count()
            });
        }

        result.Candidates = result.Candidates
            .OrderBy(c => c.Rank)
            .ToList();

        result.TopCandidate = result.Candidates.FirstOrDefault();

        result.TotalCandidates = result.Candidates.Count;
        result.CandidatesWithEvidence = result.Candidates.Count(c => c.Evidence.Count > 0);
        result.CandidatesWithoutEvidence = result.TotalCandidates - result.CandidatesWithEvidence;

        return result;
    }
}