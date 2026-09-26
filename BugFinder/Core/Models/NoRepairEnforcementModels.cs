using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-14.9: Strict No-Repair Enforcement Models
/// Structural guards that keep the Bug Finder Core provably read-only.
/// </summary>

/// <summary>Every operation the Core could be asked to perform.</summary>
public enum CoreOperation
{
    // ALLOWED (canonical Strict Core Boundary)
    ReadSource, ReadTests, ReadTestResults, ReadLogs, ReadRuntimeData,
    ReadCoverage, ReadGitHistory, ReadGitDiff, AnalyzeAst, AnalyzeSemanticModel,
    AnalyzeSymbols, AnalyzeDependencies, CorrelateEvidence, CalculateSuspiciousness,
    RankCandidates, GenerateInvestigationReport,

    // FORBIDDEN in Core
    ModifySource, GeneratePatch, ApplyPatch, CommitRepair, RollbackRepair,
    GenerateReplacementCode, AutoFix, AutoRepair, RunIterativeRepairLoop,
    ExecuteRemediation
}

public enum CoreOperationVerdict { Allowed, Blocked }

public class OperationVerdict
{
    public CoreOperation Operation { get; set; }
    public CoreOperationVerdict Verdict { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public enum CoreClaimType { Observation, CandidateSuspicion, RootCause }
public enum ClaimAcceptance { Accepted, Rejected }

/// <summary>Input for claim validation (Stage 4: Require evidence + Preserve uncertainty).</summary>
public class EvidenceClaimRequest
{
    public CoreClaimType ClaimType { get; set; }
    public string ClaimText { get; set; } = string.Empty;
    public string? TargetKey { get; set; }
    public double ConfidenceScore { get; set; }
    public int DistinctSourceCount { get; set; }
    public int UnresolvedHighConflicts { get; set; }
}

public class ClaimVerdict
{
    public EvidenceClaimRequest Request { get; set; } = new();
    public ClaimAcceptance Acceptance { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool UncertaintyPreserved { get; set; }
}

/// <summary>Stage 5: the only conclusion shape the Core may emit.</summary>
public class EvidenceOnlyConclusion
{
    public FailureIdentityInput? Failure { get; set; }
    public List<string> EvidenceItems { get; set; } = new();
    public List<string> RankedCandidates { get; set; } = new();
    public List<string> Limitations { get; set; } = new();

    /// <summary>Structural marker: this type carries no repair payload by design.</summary>
    public const bool CarriesNoRepairPayload = true;

    public const string Disclaimer =
        "Evidence-only conclusion: the Core reports evidence, candidates, and limitations - never fixes.";
}

/// <summary>Stage 1 audit: reflection finding of repair-capable API vocabulary.</summary>
public class WriteCapabilityFinding
{
    public string TypeName { get; set; } = string.Empty;
    public string MemberName { get; set; } = string.Empty;
    public string MatchedToken { get; set; } = string.Empty;
}