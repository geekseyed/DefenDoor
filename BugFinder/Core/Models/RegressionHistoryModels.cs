namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-10.3 & BF-10.4: Regression History Models
/// </summary>

public class RegressionSearchResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    // For BF-10.3: Last Passing
    public string? LastPassingCommitSha { get; set; }
    public DateTime? LastPassingTimestamp { get; set; }
    public int CommitsSearched { get; set; }

    // For BF-10.4: First Failing
    public string? FirstFailingCommitSha { get; set; }
    public DateTime? FirstFailingTimestamp { get; set; }

    // Common
    public List<CommitTestResult> TestedCommits { get; set; } = new();
    public SearchStrategy Strategy { get; set; }
}

public class CommitTestResult
{
    public string CommitSha { get; set; } = string.Empty;
    public string ShortSha { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public TestStatus Status { get; set; }
    public int TotalTests { get; set; }
    public int PassedTests { get; set; }
    public int FailedTests { get; set; }
    public int SkippedTests { get; set; }
    public TimeSpan TestDuration { get; set; }
    public string? FailureMessage { get; set; }
}

public enum TestStatus
{
    Unknown,
    Passed,
    Failed,
    Skipped,
    Error,
    Timeout
}

public enum SearchStrategy
{
    LinearBackward,      // Simple linear search from HEAD backward
    BinarySearch,        // Binary search between two points
    Hybrid               // Linear with early exit
}