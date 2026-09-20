namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-07.2: Retry Strategy & Reproduction Models
/// </summary>

public class RetryStrategy
{
    public int MaxRetries { get; set; } = 3;
    public int DelayMs { get; set; } = 100; // Delay between retries
    public RetryCondition Condition { get; set; } = RetryCondition.OnFlakyFailure;
}

public enum RetryCondition
{
    OnAnyFailure,
    OnFlakyFailure,      // Only if test is known to be flaky
    OnInfrastructureError // Only for specific error patterns (e.g., Timeout, Network)
}

public class ReproductionScript
{
    public string TestId { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty; // e.g., "dotnet test --filter FullyQualifiedName~MyTest"
    public List<string> EnvironmentVariables { get; set; } = new();
    public string? Notes { get; set; } // e.g., "Run this 5 times to reproduce flakiness"

    public override string ToString()
    {
        var envVars = string.Join(" ", EnvironmentVariables.Select(e => $"-e {e}"));
        return $"{Command} {envVars}".Trim();
    }
}

public class RetryResult
{
    public bool IsSuccess { get; set; }
    public int AttemptsMade { get; set; }
    public List<TestOutcome> AttemptOutcomes { get; set; } = new();
    public string? FinalErrorMessage { get; set; }

    public bool IsConfirmedFlaky => AttemptOutcomes.Any(o => o == TestOutcome.Passed) &&
                                    AttemptOutcomes.Any(o => o == TestOutcome.Failed);
}