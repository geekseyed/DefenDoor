using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class NoRepairEnforcementServiceTests
{
    private readonly NoRepairEnforcementService _service = new();
    private static readonly DateTime T0 = new(2026, 9, 20, 10, 0, 0, DateTimeKind.Utc);

    private static readonly FailureIdentityInput Failure = new()
    {
        TestIdentity = "ISCM.Tests.EventLogSizeCheck_Test",
        FailureCategory = "Assertion:Equality"
    };

    private static readonly List<FailureLocation> PrimaryLocation = new()
    {
        new() { FilePath = "Check.cs", LineNumber = 42, MethodName = "Evaluate", IsPrimary = true }
    };

    private static readonly CoreOperation[] AllowedOps =
    {
        CoreOperation.ReadSource, CoreOperation.ReadTests, CoreOperation.ReadTestResults,
        CoreOperation.ReadLogs, CoreOperation.ReadRuntimeData, CoreOperation.ReadCoverage,
        CoreOperation.ReadGitHistory, CoreOperation.ReadGitDiff, CoreOperation.AnalyzeAst,
        CoreOperation.AnalyzeSemanticModel, CoreOperation.AnalyzeSymbols,
        CoreOperation.AnalyzeDependencies, CoreOperation.CorrelateEvidence,
        CoreOperation.CalculateSuspiciousness, CoreOperation.RankCandidates,
        CoreOperation.GenerateInvestigationReport
    };

    private static readonly CoreOperation[] ForbiddenOps =
    {
        CoreOperation.ModifySource, CoreOperation.GeneratePatch, CoreOperation.ApplyPatch,
        CoreOperation.CommitRepair, CoreOperation.RollbackRepair,
        CoreOperation.GenerateReplacementCode, CoreOperation.AutoFix, CoreOperation.AutoRepair,
        CoreOperation.RunIterativeRepairLoop, CoreOperation.ExecuteRemediation
    };

    // Stages 2-3 — operation gate
    [Fact]
    public void ValidateOperation_AllowedOperations_Permitted()
    {
        foreach (var op in AllowedOps)
        {
            var verdict = _service.ValidateOperation(op);
            verdict.Verdict.Should().Be(CoreOperationVerdict.Allowed, $"because {op} is canonical-read");
            verdict.Reason.Should().NotBeEmpty();
        }
    }

    [Fact]
    public void ValidateOperation_ForbiddenOperations_Blocked()
    {
        foreach (var op in ForbiddenOps)
        {
            var verdict = _service.ValidateOperation(op);
            verdict.Verdict.Should().Be(CoreOperationVerdict.Blocked, $"because {op} violates the boundary");
            verdict.Reason.Should().Contain("forbidden").And.Contain("Strict Core Boundary");
        }
    }

    // Stage 1 — file policy
    [Fact]
    public void FilePolicy_ReadAllowed_WriteDeletePatchBlocked()
    {
        _service.PermitFileRead("src/Check.cs").Verdict.Should().Be(CoreOperationVerdict.Allowed);
        _service.PermitFileWrite("src/Check.cs").Verdict.Should().Be(CoreOperationVerdict.Blocked);
        _service.PermitFileDelete("src/Check.cs").Verdict.Should().Be(CoreOperationVerdict.Blocked);
        _service.PermitPatchApply("fix.patch").Verdict.Should().Be(CoreOperationVerdict.Blocked);
        _service.PermitFileWrite("src/Check.cs").Reason.Should().Contain("read-only");
    }

    // Stage 4 — claims
    [Fact]
    public void Claim_Observation_AcceptedWithoutEvidence()
    {
        var verdict = _service.ValidateClaim(new EvidenceClaimRequest
        {
            ClaimType = CoreClaimType.Observation,
            ClaimText = "test X failed"
        });

        verdict.Acceptance.Should().Be(ClaimAcceptance.Accepted);
    }

    [Fact]
    public void Claim_CandidateSuspicion_WithoutEvidence_Rejected()
    {
        var verdict = _service.ValidateClaim(new EvidenceClaimRequest
        {
            ClaimType = CoreClaimType.CandidateSuspicion,
            ClaimText = "probably T1",
            ConfidenceScore = 0
        });

        verdict.Acceptance.Should().Be(ClaimAcceptance.Rejected);
        verdict.UncertaintyPreserved.Should().BeTrue();
    }

    [Fact]
    public void Claim_CandidateSuspicion_WithEvidence_Accepted()
    {
        var verdict = _service.ValidateClaim(new EvidenceClaimRequest
        {
            ClaimType = CoreClaimType.CandidateSuspicion,
            ClaimText = "T1 suspicious",
            TargetKey = "T1",
            ConfidenceScore = 0.5,
            DistinctSourceCount = 1
        });

        verdict.Acceptance.Should().Be(ClaimAcceptance.Accepted);
    }

    [Fact]
    public void Claim_RootCause_Unsupported_RejectedWithUncertaintyPreserved()
    {
        var verdict = _service.ValidateClaim(new EvidenceClaimRequest
        {
            ClaimType = CoreClaimType.RootCause,
            ClaimText = "T1 is THE cause",
            TargetKey = "T1",
            ConfidenceScore = 0.5,
            DistinctSourceCount = 1,
            UnresolvedHighConflicts = 1
        });

        verdict.Acceptance.Should().Be(ClaimAcceptance.Rejected);
        verdict.UncertaintyPreserved.Should().BeTrue();
        verdict.Reason.Should().Contain("unsupported root cause");
    }

    [Fact]
    public void Claim_RootCause_StrongNoConflicts_ConditionallyAccepted()
    {
        var verdict = _service.ValidateClaim(new EvidenceClaimRequest
        {
            ClaimType = CoreClaimType.RootCause,
            ClaimText = "T1 cause (strong)",
            TargetKey = "T1",
            ConfidenceScore = 0.85,
            DistinctSourceCount = 3,
            UnresolvedHighConflicts = 0
        });

        verdict.Acceptance.Should().Be(ClaimAcceptance.Accepted);
        verdict.Reason.Should().Contain("human confirmation");
    }

    [Fact]
    public void Claim_RootCause_StrongButHighConflict_Rejected()
    {
        var verdict = _service.ValidateClaim(new EvidenceClaimRequest
        {
            ClaimType = CoreClaimType.RootCause,
            ClaimText = "T1 cause?",
            TargetKey = "T1",
            ConfidenceScore = 0.85,
            DistinctSourceCount = 3,
            UnresolvedHighConflicts = 1
        });

        verdict.Acceptance.Should().Be(ClaimAcceptance.Rejected);
    }

    // Stage 5 — evidence-only conclusion from a real investigation
    [Fact]
    public void Conclusion_FromCleanInvestigation_EvidenceCandidatesLimitations()
    {
        var raw = new[]
        {
            new FusionEvidenceInput { SourceType = CandidateEvidenceType.Stack,    RawStrength = 0.9, TargetSymbolKey = "M|ISCM|Check.Evaluate()", TargetFilePath = "Check.cs", ObservedAtUtc = T0 },
            new FusionEvidenceInput { SourceType = CandidateEvidenceType.Coverage, RawStrength = 0.7, TargetSymbolKey = "M|ISCM|Check.Evaluate()", TargetFilePath = "Check.cs", ObservedAtUtc = T0.AddMinutes(5) }
        };
        var report = new InvestigationReportService().Investigate(Failure, PrimaryLocation, raw);

        var conclusion = _service.BuildEvidenceOnlyConclusion(report);

        conclusion.Failure.Should().BeSameAs(report.Failure);
        conclusion.EvidenceItems.Should().Contain(e => e.Contains("inputs=2"));
        conclusion.EvidenceItems.Should().Contain(e => e.Contains("conflicts=0"));
        conclusion.RankedCandidates.Should().ContainSingle()
            .Which.Should().Contain("M|ISCM|Check.Evaluate()").And.Contain("VeryHigh");
        conclusion.Limitations.Should().HaveCount(4);   // 3 disclaimers + core boundary
        conclusion.Limitations.Should().Contain(InvestigationReport.CoreBoundary);
        EvidenceOnlyConclusion.CarriesNoRepairPayload.Should().BeTrue();
    }

    [Fact]
    public void Conclusion_FromNullReport_EmptyTyped()
    {
        var conclusion = _service.BuildEvidenceOnlyConclusion(null);

        conclusion.Failure.Should().BeNull();
        conclusion.EvidenceItems.Should().BeEmpty();
        conclusion.RankedCandidates.Should().BeEmpty();
        conclusion.Limitations.Should().BeEmpty();
    }

    // Stage 1 — structural audit of the Core assembly
    [Fact]
    public void Audit_CoreAssembly_NoWriteCapableApis()
    {
        var findings = _service.AuditAssemblyForWriteCapabilities();

        findings.Should().BeEmpty(
            "the Bug Finder Core must contain no repair-capable API vocabulary");
    }
}