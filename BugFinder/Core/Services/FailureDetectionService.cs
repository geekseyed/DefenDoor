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
            var failureType = DetermineTestFailureType(test);

            failures.Add(new Failure
            {
                Identity = test.Identity,
                Type = failureType,
                Message = test.ErrorMessage ?? "Unknown test failure",
                StackTrace = test.StackTrace,
                SourceTestId = test.Identity.ToFullString(),
                DetectedAt = DateTime.UtcNow,
                Localization = ExtractLocalization(test)
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
                },
                // Domain failures usually don't have direct source localization yet
                Localization = null
            });
        }

        return failures;
    }

    private static FailureType DetermineTestFailureType(NormalizedTestResult test)
    {
        if (string.IsNullOrEmpty(test.ErrorMessage))
            return FailureType.TestFailure;

        var msg = test.ErrorMessage.ToLower();

        // Detect Timeout
        if (msg.Contains("timeout") || msg.Contains("timed out"))
            return FailureType.TimeoutFailure;

        // Detect Infrastructure issues (e.g., file not found, access denied, connection refused)
        if (msg.Contains("access denied") ||
            msg.Contains("file not found") ||
            msg.Contains("connection refused") ||
            msg.Contains("ioexception"))
            return FailureType.InfrastructureFailure;

        // Detect Exception-based failures
        if (!string.IsNullOrEmpty(test.StackTrace))
            return FailureType.ExceptionFailure;

        // Default to generic TestFailure (e.g., assertion failure without exception)
        return FailureType.TestFailure;
    }

    private static FailureLocalization? ExtractLocalization(NormalizedTestResult test)
    {
        if (string.IsNullOrEmpty(test.StackTrace))
            return null;

        // Simple heuristic: Take the first application frame as primary location
        // In BF-03, this will be replaced by a robust StackTraceParser
        var lines = test.StackTrace.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        foreach (var line in lines)
        {
            // Look for patterns like "in C:\Path\File.cs:line 42" or "at Namespace.Class.Method in File.cs:line 42"
            // This is a simplified placeholder for BF-03's full parser
            var fileMatch = System.Text.RegularExpressions.Regex.Match(line, @"in\s+(.+\.cs):line\s+(\d+)");
            if (fileMatch.Success)
            {
                return new FailureLocalization
                {
                    PrimaryFilePath = fileMatch.Groups[1].Value,
                    PrimaryLineNumber = int.Parse(fileMatch.Groups[2].Value),
                    Confidence = LocalizationConfidence.High,
                    MethodName = ExtractMethodName(line)
                };
            }

            // Fallback: Just method name
            var methodMatch = System.Text.RegularExpressions.Regex.Match(line, @"at\s+(.+)");
            if (methodMatch.Success && string.IsNullOrEmpty(fileMatch.Value))
            {
                return new FailureLocalization
                {
                    MethodName = methodMatch.Groups[1].Value,
                    Confidence = LocalizationConfidence.Low
                };
            }
        }

        return null;
    }

    private static string? ExtractMethodName(string stackLine)
    {
        var match = System.Text.RegularExpressions.Regex.Match(stackLine, @"at\s+(.+?)\s+in");
        if (match.Success) return match.Groups[1].Value;

        match = System.Text.RegularExpressions.Regex.Match(stackLine, @"at\s+(.+)");
        return match.Success ? match.Groups[1].Value : null;
    }
}