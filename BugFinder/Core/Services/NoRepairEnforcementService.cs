using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-14.9: Strict No-Repair Enforcement Service
/// Stage 1: No source modification - file policy (reads allowed,
///          writes/deletes/patch-apply blocked) + reflection audit of
///          the Core assembly for repair-capable API vocabulary
/// Stages 2-3: No auto-fix / no remediation - operation gate over the
///          canonical allowed (16) and forbidden (10) operation sets
/// Stage 4: No unsupported root cause - claim validator; every
///          rejection preserves uncertainty explicitly
/// Stage 5: Evidence-only conclusion - evidence + candidates +
///          limitations, structurally free of repair payload
/// </summary>
public class NoRepairEnforcementService
{
    public const string BoundaryReference =
        "Strict Core Boundary (BF-00.4 / BF-14.9): the Core reads, analyzes, correlates, and reports. " +
        "It never modifies source, generates or applies patches, executes remediation, " +
        "or declares unsupported root causes.";

    public const double RootCauseMinConfidence = 0.8;
    public const int RootCauseMinDistinctSources = 3;

    private static readonly HashSet<CoreOperation> ForbiddenOperations = new()
    {
        CoreOperation.ModifySource, CoreOperation.GeneratePatch, CoreOperation.ApplyPatch,
        CoreOperation.CommitRepair, CoreOperation.RollbackRepair,
        CoreOperation.GenerateReplacementCode, CoreOperation.AutoFix, CoreOperation.AutoRepair,
        CoreOperation.RunIterativeRepairLoop, CoreOperation.ExecuteRemediation
    };

    private static readonly string[] ForbiddenNameTokens =
        { "patch", "autofix", "autorepair", "repair", "remediat", "rollback", "modifysource" };

    // Stages 2-3 — operation gate
    public OperationVerdict ValidateOperation(CoreOperation operation)
    {
        var forbidden = ForbiddenOperations.Contains(operation);
        return new OperationVerdict
        {
            Operation = operation,
            Verdict = forbidden ? CoreOperationVerdict.Blocked : CoreOperationVerdict.Allowed,
            Reason = forbidden
                ? $"{operation} is forbidden in the Core. {BoundaryReference}"
                : $"{operation} is a read/analyze/report operation permitted by the Strict Core Boundary."
        };
    }

    // Stage 1 — file access policy at the API boundary
    public OperationVerdict PermitFileRead(string path) => new()
    {
        Operation = CoreOperation.ReadSource,
        Verdict = CoreOperationVerdict.Allowed,
        Reason = $"read of '{path}' permitted (read-only workspace policy)"
    };

    public OperationVerdict PermitFileWrite(string path) => BlockedFileOp(path, "write");
    public OperationVerdict PermitFileDelete(string path) => BlockedFileOp(path, "delete");
    public OperationVerdict PermitPatchApply(string path) => BlockedFileOp(path, "patch application");

    private static OperationVerdict BlockedFileOp(string path, string action) => new()
    {
        Operation = CoreOperation.ModifySource,
        Verdict = CoreOperationVerdict.Blocked,
        Reason = $"{action} on '{path}' is blocked: the Core is read-only. {BoundaryReference}"
    };

    // Stage 4 — claim validation
    public ClaimVerdict ValidateClaim(EvidenceClaimRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        switch (request.ClaimType)
        {
            case CoreClaimType.Observation:
                return Accept(request, "observation of recorded evidence requires no corroboration");

            case CoreClaimType.CandidateSuspicion:
                var hasSignal = request.ConfidenceScore > 0
                                && !string.IsNullOrWhiteSpace(request.TargetKey);
                return hasSignal
                    ? Accept(request, "candidate suspicion backed by at least one evidence signal")
                    : Reject(request, "candidate suspicion without any evidence signal is rejected; uncertainty preserved");

            case CoreClaimType.RootCause:
                var supported = request.ConfidenceScore >= RootCauseMinConfidence
                                && request.DistinctSourceCount >= RootCauseMinDistinctSources
                                && request.UnresolvedHighConflicts == 0;
                return supported
                    ? Accept(request,
                        $"root-cause claim conditionally accepted (confidence >= {RootCauseMinConfidence}, " +
                        $"{RootCauseMinDistinctSources}+ distinct sources, no unresolved high conflicts); " +
                        "requires human confirmation - the Core executes no repair")
                    : Reject(request,
                        $"unsupported root cause: requires confidence >= {RootCauseMinConfidence}, " +
                        $">= {RootCauseMinDistinctSources} distinct sources, and no unresolved high conflicts; " +
                        "uncertainty preserved");

            default:
                return Reject(request, "unknown claim type");
        }
    }

    // Stage 5 — evidence-only conclusion (Atomic 1-3)
    public EvidenceOnlyConclusion BuildEvidenceOnlyConclusion(InvestigationReport? report)
    {
        var conclusion = new EvidenceOnlyConclusion();
        if (report is null) return conclusion;

        conclusion.Failure = report.Failure;

        // Atomic 1 — report evidence
        if (report.Evidence is not null)
        {
            conclusion.EvidenceItems.Add(
                $"inputs={report.Evidence.TotalInputs}, targets={report.Evidence.TotalTargets}, " +
                $"corroborated={report.Evidence.FullyCorroboratedCount}");
            if (report.Evidence.ConsistencyStatus.HasValue)
                conclusion.EvidenceItems.Add($"consistency={report.Evidence.ConsistencyStatus.Value}");
            conclusion.EvidenceItems.Add(
                $"conflicts={report.Evidence.ConflictCount} (unresolved: {report.Evidence.UnresolvedConflictCount})");
        }
        if (report.Locations.Count > 0)
            conclusion.EvidenceItems.Add($"failure localized to {report.Locations.Count} location(s)");

        // Atomic 2 — report candidates (joined with confidence)
        var confidenceByTarget = (report.Confidence?.Targets ?? new List<TargetConfidence>())
            .ToDictionary(t => t.TargetKey, t => t);
        foreach (var suspicious in report.SuspiciousLocations)
        {
            confidenceByTarget.TryGetValue(suspicious.TargetKey, out var conf);
            conclusion.RankedCandidates.Add(
                $"{suspicious.TargetKey} (signals={suspicious.SupportingSignalCount}, " +
                $"confidence={conf?.Level.ToString() ?? "Unknown"})");
        }

        // Atomic 3 — report limitations
        conclusion.Limitations.Add(InvestigationReport.DisclaimerSuspiciousness);
        conclusion.Limitations.Add(InvestigationReport.DisclaimerConfidence);
        conclusion.Limitations.Add(InvestigationReport.DisclaimerUncertainty);
        conclusion.Limitations.Add(InvestigationReport.CoreBoundary);
        if (report.Uncertainty is not null)
            foreach (var t in report.Uncertainty.Targets.Where(t => t.MissingEvidence.Count > 0))
                conclusion.Limitations.Add($"{t.TargetKey}: missing {t.MissingEvidence.Count} evidence dimension(s)");
        if (report.Outcome == InvestigationOutcome.InsufficientEvidence)
            conclusion.Limitations.Add("investigation did not run: insufficient inputs");

        return conclusion;
    }

    // Stage 1 Atomic 1 — reflection audit for repair-capable API vocabulary
    public List<WriteCapabilityFinding> AuditAssemblyForWriteCapabilities()
    {
        var assembly = typeof(NoRepairEnforcementService).Assembly;
        var findings = new List<WriteCapabilityFinding>();

        foreach (var type in assembly.GetTypes())
        {
            if (type.Name.StartsWith("<", StringComparison.Ordinal)) continue;              // compiler-generated
            if (type.FullName?.Contains("NoRepair") == true) continue;                      // the enforcement itself
            if (type.Namespace?.StartsWith("ISCM.BugFinder.Core", StringComparison.Ordinal) != true) continue;

            void Scan(string memberName)
            {
                foreach (var token in ForbiddenNameTokens)
                {
                    if (memberName.Contains(token, StringComparison.OrdinalIgnoreCase))
                    {
                        findings.Add(new WriteCapabilityFinding
                        {
                            TypeName = type.Name,
                            MemberName = memberName,
                            MatchedToken = token
                        });
                    }
                }
            }

            Scan(type.Name);
            foreach (var method in type.GetMethods()) Scan(method.Name);
            foreach (var property in type.GetProperties()) Scan(property.Name);
        }

        return findings;
    }

    private static ClaimVerdict Accept(EvidenceClaimRequest request, string reason) => new()
    {
        Request = request,
        Acceptance = ClaimAcceptance.Accepted,
        Reason = reason,
        UncertaintyPreserved = false
    };

    private static ClaimVerdict Reject(EvidenceClaimRequest request, string reason) => new()
    {
        Request = request,
        Acceptance = ClaimAcceptance.Rejected,
        Reason = reason,
        UncertaintyPreserved = true
    };
}