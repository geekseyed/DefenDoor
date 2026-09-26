using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-15.4: Dynamic Slicing Service
/// Stage 1 Failure Variable: caller supplies the wrong variable + the
///          observation operation; the trace is a caller contract.
/// Stage 2 Dependency Tracking: def/use per operation + ControlVariable
///          (control dependence modeled as the branch variable's data dep).
/// Stage 3 Execution Slice: single backward scan from the observation
///          point; an operation is included when it writes a needed
///          variable; its reads and control variable become needed.
/// Stage 4 Failure-Relevant Slice: reaching-def semantics - once a
///          needed variable's nearest earlier writer is included, older
///          writes of the same variable are killed and excluded.
/// Comparison uses ordinal (variable names are code identifiers).
/// </summary>
public class DynamicSliceService
{
    public const string ResearchBoundaryNote =
        "BF-15.4 research output: dynamic slice over a caller-supplied trace. " +
        "Read-only Core - no instrumentation, no execution, no source modification.";

    public DynamicSliceReport Slice(
        string scenarioName,
        IReadOnlyList<TraceOperation> trace,
        FailureVariableInput failure)
    {
        if (scenarioName is null) throw new ArgumentNullException(nameof(scenarioName));
        if (trace is null) throw new ArgumentNullException(nameof(trace));
        if (failure is null) throw new ArgumentNullException(nameof(failure));
        if (string.IsNullOrWhiteSpace(failure.VariableName))
            throw new ArgumentException("Failure variable name is required.", nameof(failure));
        if (string.IsNullOrWhiteSpace(failure.AtOperationId))
            throw new ArgumentException("Failure operation id is required.", nameof(failure));

        var report = new DynamicSliceReport
        {
            ScenarioName = scenarioName,
            OriginalOperationCount = trace.Count
        };

        // Stage 1 - empty trace
        if (trace.Count == 0)
        {
            report.Status = DynamicSliceStatus.Empty;
            return report;
        }

        // Stage 1 - locate the observation point (first occurrence wins)
        var failureIndex = -1;
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < trace.Count; i++)
        {
            seenIds.Add(trace[i].OperationId);
            if (failureIndex < 0 && trace[i].OperationId == failure.AtOperationId)
                failureIndex = i;
        }

        if (failureIndex < 0)
        {
            report.Status = DynamicSliceStatus.UnresolvedFailureOperation;
            return report;
        }

        var failureOp = trace[failureIndex];

        // Stage 3 - include the observation point
        var included = new HashSet<string>(StringComparer.Ordinal) { failureOp.OperationId };
        var needed = new HashSet<string>(StringComparer.Ordinal);

        // If the observation point itself writes the failure variable, the
        // wrongness originates in its inputs - no earlier def is needed.
        if (!string.Equals(failureOp.WrittenVariable, failure.VariableName, StringComparison.Ordinal))
            needed.Add(failure.VariableName);
        foreach (var read in failureOp.ReadVariables) needed.Add(read);
        if (failureOp.ControlVariable is not null) needed.Add(failureOp.ControlVariable);

        // Stages 3-4 - backward reaching-def scan (single pass = fixpoint)
        for (var i = failureIndex - 1; i >= 0; i--)
        {
            var op = trace[i];
            if (op.WrittenVariable is null || !needed.Contains(op.WrittenVariable))
                continue;

            included.Add(op.OperationId);
            needed.Remove(op.WrittenVariable);
            foreach (var read in op.ReadVariables) needed.Add(read);
            if (op.ControlVariable is not null) needed.Add(op.ControlVariable);
        }

        // Stage 4 - report in execution order
        report.Status = DynamicSliceStatus.Sliced;
        report.SliceOperations = trace.Where(op => included.Contains(op.OperationId)).ToList();
        report.SliceOperationIds = report.SliceOperations.Select(op => op.OperationId).ToList();
        report.ExcludedOperationIds = trace
            .Select(op => op.OperationId)
            .Where(id => !included.Contains(id))
            .ToList();
        report.SliceOperationCount = report.SliceOperations.Count;
        report.SliceRatio = report.OriginalOperationCount == 0
            ? 0.0
            : 1.0 - ((double)report.SliceOperationCount / report.OriginalOperationCount);
        report.IsBackwardClosed = true;

        return report;
    }
}