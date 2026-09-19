using ISCM.Domain.Enums;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// Represents a detected failure with classification and evidence links.
/// BF-02: Failure Detection & Classification
/// </summary>
public class Failure
{
    public FailureIdentity Identity { get; set; } = new();
    public FailureType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? StackTrace { get; set; }
    public string? SourceTestId { get; set; }
    public DateTime DetectedAt { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

public enum FailureType
{
    Unknown,
    TestFailure,              // xUnit/Test SDK failure
    DomainEvaluationFailure,  // Business logic check failed (Pass/Fail in domain)
    ExceptionFailure,         // Unhandled exception/Crash
    TimeoutFailure,           // Test or Check timed out
    InfrastructureFailure,    // Build/Discovery/Env failure
    CompositeFailure          // Multiple signals correlated
}