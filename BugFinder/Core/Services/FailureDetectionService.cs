using ISCM.BugFinder.Core.Models;
using ISCM.Domain.Enums;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-02: Failure Detection & Classification
/// Detects and classifies failures from a normalized session.
/// </summary>
public class FailureDetectionService
{
    public List<Failure> DetectFailures(BugFinderSession session)
    {
        var failures = new List<Failure>();

        // 1. Detect Test Failures (xUnit level)
        foreach (var test in session.TestExecutions.Where(t => t.Outcome == TestOutcome.Failed))
        {
            failures.Add(new Failure
            {
                Identity = test.Identity,
                Type = FailureType.TestFailure,
                Message = test.ErrorMessage ?? "Unknown test failure",
                StackTrace = test.StackTrace,
                SourceTestId = test.Identity.ToFullString(),
                DetectedAt = DateTime.UtcNow
            });
        }

        // 2. Detect Domain Evaluation Failures (Application Logic level)
        foreach (var eval in session.EvaluationResults.Where(e => e.Status == CheckStatus.Fail))
        {
            failures.Add(new Failure
            {
                Identity = new FailureIdentity
                {
                    TestName = eval.SubControlId,
                    ClassName = "DomainEvaluation",
                    AssemblyName = "ISCM.Application"
                },
                Type = FailureType.DomainEvaluationFailure,
                Message = eval.Reason ?? $"Evaluation failed for {eval.SubControlId}",
                SourceTestId = eval.SourceTestId,
                DetectedAt = DateTime.UtcNow,
                Metadata = new Dictionary<string, string>
                {
                    { "SubControlId", eval.SubControlId },
                    { "Expected", eval.Expected ?? "N/A" },
                    { "Actual", eval.Actual ?? "N/A" }
                }
            });
        }

        // 3. Detect Exception Failures (Infrastructure/Crash level)
        // (Can be expanded later to parse ConsoleError for specific exception patterns)
        if (!string.IsNullOrWhiteSpace(session.Artifacts.FirstOrDefault()))
        {
            // Placeholder for future exception parsing logic
        }

        return failures;
    }
}

// Note: FailureType enum is defined in Models.FailureType to avoid ambiguity.
// Note: Failure class is defined in Models.FailureModels.