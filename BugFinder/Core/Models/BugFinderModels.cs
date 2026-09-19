
using ISCM.Domain.Enums;


namespace ISCM.BugFinder.Core.Models;

public class BugFinderSession
{
    public string SessionId { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime EndedAt { get; set; }
    public List<NormalizedTestResult> TestExecutions { get; set; } = new();
    public List<NormalizedEvaluationResult> EvaluationResults { get; set; } = new();
    public List<string> Artifacts { get; set; } = new();
}

public class NormalizedTestResult
{
    public FailureIdentity Identity { get; set; } = new();
    public TestOutcome Outcome { get; set; }
    public TimeSpan Duration { get; set; }
    public DateTime ExecutedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? StackTrace { get; set; }
}

public class NormalizedEvaluationResult
{
    public string SubControlId { get; set; } = string.Empty;
    public CheckStatus Status { get; set; }
    public string? Reason { get; set; }
    public string? Expected { get; set; }
    public string? Actual { get; set; }
    public string? SourceTestId { get; set; }
}

public class FailureIdentity
{
    public string TestName { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string AssemblyName { get; set; } = string.Empty;

    public string ToFullString() => $"{AssemblyName}:{ClassName}.{TestName}";
}

public enum TestOutcome
{
    Unknown,
    Passed,
    Failed,
    Skipped
}