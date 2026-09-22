using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-12.10: Candidate Ranking Models
/// Final output stage of the Fault Localization Engine (BF-12).
/// </summary>

/// <summary>
/// Types of evidence that can support a ranked candidate.
/// External signals (Coverage/Stack/Regression/Historical) are optional hooks
/// for outputs of BF-03/BF-06/BF-10/BF-11 when available.
/// </summary>
public enum CandidateEvidenceType
{
    Sbfl,           // BF-12.4-12.6 spectrum score
    MultiTest,      // BF-12.7 consensus
    DomainFailure,  // BF-12.8 domain evaluation failure
    Coverage,       // BF-06 coverage data (optional)
    Stack,          // BF-03 stack localization (optional)
    Regression,     // BF-10 regression boundary (optional)
    Historical      // BF-11 recurring failure (optional)
}

/// <summary>
/// A single evidence reference attached to a ranked candidate.
/// </summary>
public class CandidateEvidenceRef
{
    public CandidateEvidenceType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? SourceArtifact { get; set; }   // e.g., TRX path, coverage file, commit SHA
    public double Strength { get; set; }          // 0.0 - 1.0
}

/// <summary>
/// A ranked candidate enriched with its supporting evidence.
/// </summary>
public class EvidenceRankedCandidate
{
    public string ElementId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int? LineNumber { get; set; }

    public double UnifiedScore { get; set; }
    public int Rank { get; set; }
    public string ConfidenceLevel { get; set; } = string.Empty;

    public List<CandidateEvidenceRef> Evidence { get; set; } = new();

    /// <summary>How many different signal types support this candidate.</summary>
    public int DistinctSignalCount { get; set; }
}

/// <summary>
/// The final Ranked Localization Result — the official output of BF-12.
/// Feeds BF-13 (Static Correlation) and BF-14 (Investigation Report).
/// </summary>
public class RankedLocalizationResult
{
    public List<EvidenceRankedCandidate> Candidates { get; set; } = new();
    public EvidenceRankedCandidate? TopCandidate { get; set; }

    public int TotalCandidates { get; set; }
    public int CandidatesWithEvidence { get; set; }
    public int CandidatesWithoutEvidence { get; set; }

    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Non-negotiable architectural principle of BF-12.</summary>
    public const string Disclaimer =
        "Suspiciousness != Root Cause. Scores are investigative signals, not confirmed causes.";
}