using ISCM.BugFinder.Core.Models;
using ISCM.Domain.Enums;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-02: Failure Detection & Classification
/// BF-03: Integrated Localization
/// Detects failures and enriches them with source code coordinates.
/// </summary>
public class FailureDetectionService
{
    private readonly StackTraceParser _stackTraceParser;
    private readonly LocalizationService _localizationService;

    public FailureDetectionService()
    {
        _stackTraceParser = new StackTraceParser();
        _localizationService = new LocalizationService();
    }

    public List<Failure> DetectFailures(BugFinderSession session)
    {
        var failures = new List<Failure>();

        // 1. Detect Test Failures (xUnit level) + Localization
        foreach (var test in session.TestExecutions.Where(t => t.Outcome == TestOutcome.Failed))
        {
            var failure = new Failure
            {
                Identity = test.Identity,
                Type = FailureType.TestFailure,
                Message = test.ErrorMessage ?? "Unknown test failure",
                StackTrace = test.StackTrace,
                SourceTestId = test.Identity.ToFullString(),
                DetectedAt = DateTime.UtcNow
            };

            // BF-03 Integration: Parse and Localize
            if (!string.IsNullOrEmpty(test.StackTrace))
            {
                var parsedTrace = _stackTraceParser.Parse(test.StackTrace);
                failure.Localization = _localizationService.Localize(parsedTrace.Frames);
            }

            failures.Add(failure);
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
                },
                // Domain failures usually don't have a stack trace in the same way tests do
                Localization = new FailureLocalization { Confidence = LocalizationConfidence.Unknown }
            });
        }

        // 3. Detect Exception Failures (Infrastructure/Crash level)
        // Logic expanded to parse ConsoleError for specific exception patterns if needed
        // For now, relying on Test Failures to capture exceptions thrown during tests

        return failures;
    }
}