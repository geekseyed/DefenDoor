using ISCM.Application.Interfaces;
using ISCM.Domain.Entities;
using ISCM.Domain.Enums;
using ISCM.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ISCM.Application.Evaluators;

public class ControlEvaluator : IControlEvaluator
{
    // =========================================================================
    // Phase 7.5: Injected typed evaluator
    // =========================================================================

    private readonly ITypedEvidenceEvaluator _typedEvidenceEvaluator;

    /// <summary>
    /// Constructor with typed evidence evaluator injection (Phase 7.5).
    /// </summary>
    public ControlEvaluator(ITypedEvidenceEvaluator typedEvidenceEvaluator)
    {
        _typedEvidenceEvaluator = typedEvidenceEvaluator
            ?? throw new ArgumentNullException(nameof(typedEvidenceEvaluator));
    }

    /// <summary>
    /// Backward-compatible parameterless constructor.
    /// </summary>
    public ControlEvaluator()
    {
        _typedEvidenceEvaluator = null!;
    }

    // =========================================================================
    // Legacy methods
    // =========================================================================

    public ControlResult Evaluate(ControlDefinition controlDefinition, IEnumerable<SubControlResult> subControlResults)
    {
        var results = subControlResults?.ToList() ?? new List<SubControlResult>();

        var controlResult = new ControlResult
        {
            ControlId = controlDefinition?.ControlId ?? "Unknown",
            EvaluatedAt = DateTime.UtcNow
        };

        if (results.Count == 0)
        {
            controlResult.Status = CheckStatus.Unknown;
            return controlResult;
        }

        controlResult.SubControlResults = results;

        var applicable = results.Where(r => r.Status != CheckStatus.NotApplicable).ToList();

        if (applicable.Count == 0)
        {
            controlResult.Status = CheckStatus.NotApplicable;
            return controlResult;
        }

        // =====================================================================
        // Phase 10.4 fix: Corrected precedence for all statuses.
        // Precedence order (highest to lowest):
        //   Error > Disagreement > Unknown > Fail > Pass
        //
        // Rationale:
        //   - Error: technical failure (overrides everything)
        //   - Disagreement: multi-path produced conflicting verdicts
        //     (more informative than Unknown, so ranks above it)
        //   - Unknown: insufficient or ambiguous evidence
        //   - Fail: at least one required SubControl failed
        //   - Pass: all applicable SubControls passed
        // =====================================================================

        if (applicable.Any(r => r.Status == CheckStatus.Error))
        {
            controlResult.Status = CheckStatus.Error;
            return controlResult;
        }

        if (applicable.Any(r => r.Status == CheckStatus.Disagreement))
        {
            controlResult.Status = CheckStatus.Disagreement;
            return controlResult;
        }

        if (applicable.Any(r => r.Status == CheckStatus.Unknown))
        {
            controlResult.Status = CheckStatus.Unknown;
            return controlResult;
        }

        if (applicable.Any(r => r.Status == CheckStatus.Fail))
        {
            controlResult.Status = CheckStatus.Fail;
            return controlResult;
        }

        if (applicable.All(r => r.Status == CheckStatus.Pass))
        {
            controlResult.Status = CheckStatus.Pass;
            return controlResult;
        }

        controlResult.Status = CheckStatus.Unknown;
        return controlResult;
    }

    public Finding EvaluateFromSubControls(ControlDefinition controlDefinition, List<SubControlResult> subControlResults, string? checkId = null)
    {
        var controlResult = Evaluate(controlDefinition, subControlResults);
        var allEvidence = subControlResults.SelectMany(s => s.EvidenceItems).ToList();

        var primaryCheckId = checkId
            ?? controlDefinition.TechnicalCheckIds.FirstOrDefault()
            ?? controlDefinition.ControlId;

        var finding = new Finding(
            checkId: primaryCheckId,
            name: controlDefinition.Title,
            category: controlDefinition.Category,
            severity: controlDefinition.Severity,
            status: controlResult.Status,
            currentValue: allEvidence.FirstOrDefault()?.RawOutput ?? "No evidence collected",
            expectedValue: subControlResults.FirstOrDefault()?.EvidenceItems.FirstOrDefault()?.ExpectedValue ?? "N/A",
            description: controlDefinition.Description,
            errorMessage: null,
            registryPath: string.Empty,
            cisReference: controlDefinition.BaselineId,
            riskScore: (int)controlDefinition.Severity * 20,
            sourceType: "Multi-Source Evidence",
            sourceCommand: string.Join(", ", allEvidence.Select(e => e.SourceName).Distinct()),
            fixTools: new List<string>(),
            subChecks: null,
            recommendation: $"Review and harden: {controlDefinition.Title}"
        );

        foreach (var evidence in allEvidence)
        {
            finding.AddEvidence(evidence);

            finding.AddTestResult(new TestResult(
                evidence.SourceType.ToString(),
                evidence.SourceName,
                evidence.Evaluation == CheckStatus.Pass,
                evidence.RawOutput
            ));
        }

        return finding;
    }

    public ControlResult EvaluateFromFindings(ControlDefinition controlDefinition, IEnumerable<Finding> findings)
    {
        var findingsList = findings?.ToList() ?? new List<Finding>();

        var subControlResults = findingsList.Select(f =>
        {
            Enum.TryParse<EvidenceSourceType>(f.SourceType, true, out var parsedSourceType);

            return new SubControlResult
            {
                SubControlId = f.CheckId,
                Status = f.Status,
                EvidenceItems = new List<Evidence>
                {
                    new Evidence
                    {
                        SourceType = parsedSourceType != EvidenceSourceType.Unknown ? parsedSourceType : EvidenceSourceType.Unknown,
                        SourceName = f.SourceCommand,
                        RawOutput = f.CurrentValue,
                        ExpectedValue = f.ExpectedValue,
                        Evaluation = f.Status,
                        CollectedAtUtc = DateTime.UtcNow
                    }
                },
                EvaluatedAt = DateTime.UtcNow
            };
        }).ToList();

        return Evaluate(controlDefinition, subControlResults);
    }

    // =========================================================================
    // Phase 7.5 — NEW: Typed SubControl evaluation
    // =========================================================================

    public SubControlEvaluationSummary EvaluateSubControlTyped(
        SubControlResult subControlResult,
        string expectedValueString,
        ExpectedValueType expectedType,
        Operator op)
    {
        if (_typedEvidenceEvaluator == null)
        {
            throw new InvalidOperationException(
                "EvaluateSubControlTyped requires a ControlEvaluator constructed with " +
                "ITypedEvidenceEvaluator. Use the DI-injected constructor.");
        }

        if (subControlResult == null)
        {
            throw new ArgumentNullException(nameof(subControlResult));
        }

        var evidenceItems = subControlResult.EvidenceItems?.ToList() ?? new List<Evidence>();

        if (evidenceItems.Count == 0)
        {
            var noEvidenceResult = EvaluationResult.Unknown(
                "SubControl has no evidence items. Cannot evaluate.",
                EvaluationResult.BuildDetails(
                    actual: "(no evidence)",
                    expected: expectedValueString,
                    op: op,
                    valueType: expectedType.ToString()));

            subControlResult.Status = CheckStatus.Unknown;
            subControlResult.EvaluatedAt = DateTime.UtcNow;

            return new SubControlEvaluationSummary(
                aggregatedStatus: CheckStatus.Unknown,
                aggregatedReason: noEvidenceResult.Reason,
                perEvidenceResults: new List<EvaluationResult> { noEvidenceResult });
        }

        var perEvidenceResults = new List<EvaluationResult>(evidenceItems.Count);

        foreach (var evidence in evidenceItems)
        {
            var result = _typedEvidenceEvaluator.Evaluate(
                evidence,
                expectedValueString,
                expectedType,
                op);

            evidence.Evaluation = result.Status;
            evidence.EvaluationReason = result.Reason;

            perEvidenceResults.Add(result);
        }

        var aggregatedStatus = AggregateStatuses(perEvidenceResults);
        var aggregatedReason = BuildAggregatedReason(perEvidenceResults, aggregatedStatus);

        subControlResult.Status = aggregatedStatus;
        subControlResult.EvaluatedAt = DateTime.UtcNow;

        return new SubControlEvaluationSummary(
            aggregatedStatus: aggregatedStatus,
            aggregatedReason: aggregatedReason,
            perEvidenceResults: perEvidenceResults);
    }

    private static CheckStatus AggregateStatuses(IReadOnlyList<EvaluationResult> results)
    {
        if (results.Count == 0)
            return CheckStatus.Unknown;

        if (results.Any(r => r.Status == CheckStatus.Error))
            return CheckStatus.Error;

        if (results.Any(r => r.Status == CheckStatus.Unknown))
            return CheckStatus.Unknown;

        if (results.Any(r => r.Status == CheckStatus.Fail))
            return CheckStatus.Fail;

        if (results.All(r => r.Status == CheckStatus.Pass))
            return CheckStatus.Pass;

        return CheckStatus.Unknown;
    }

    private static string BuildAggregatedReason(IReadOnlyList<EvaluationResult> results, CheckStatus aggregated)
    {
        var passCount = results.Count(r => r.Status == CheckStatus.Pass);
        var failCount = results.Count(r => r.Status == CheckStatus.Fail);
        var errorCount = results.Count(r => r.Status == CheckStatus.Error);
        var unknownCount = results.Count(r => r.Status == CheckStatus.Unknown);

        return $"SubControl verdict={aggregated} from {results.Count} evidence items " +
               $"(pass={passCount}, fail={failCount}, error={errorCount}, unknown={unknownCount}). " +
               $"First non-pass reason: {results.FirstOrDefault(r => r.Status != CheckStatus.Pass)?.Reason ?? "all passed"}";
    }
}

// =========================================================================
// Sub-Phase 7.5 — SubControl evaluation summary value object
// =========================================================================

public sealed class SubControlEvaluationSummary
{
    public CheckStatus AggregatedStatus { get; }
    public string AggregatedReason { get; }
    public IReadOnlyList<EvaluationResult> PerEvidenceResults { get; }

    public SubControlEvaluationSummary(
        CheckStatus aggregatedStatus,
        string aggregatedReason,
        IReadOnlyList<EvaluationResult> perEvidenceResults)
    {
        AggregatedStatus = aggregatedStatus;
        AggregatedReason = aggregatedReason ?? string.Empty;
        PerEvidenceResults = perEvidenceResults
            ?? new List<EvaluationResult>().AsReadOnly();
    }

    public bool IsPass => AggregatedStatus == CheckStatus.Pass;
    public bool IsFail => AggregatedStatus == CheckStatus.Fail;
    public bool IsError => AggregatedStatus == CheckStatus.Error;
    public bool IsUnknown => AggregatedStatus == CheckStatus.Unknown;

    public override string ToString()
        => $"[{AggregatedStatus}] {AggregatedReason}";
}