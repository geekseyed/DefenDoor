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

    // BF-03 Preflight: Separation of Identity and Localization
    public FailureLocalization? Localization { get; set; }
}

public enum FailureType
{
    Unknown,
    TestFailure,
    DomainEvaluationFailure,
    ExceptionFailure,
    TimeoutFailure,
    InfrastructureFailure,
    CompositeFailure
}

// BF-03 Preflight: Explicit Localization Model separate from Identity
public class FailureLocalization
{
    public string? MethodName { get; set; }
    public string? PrimaryFilePath { get; set; }
    public int? PrimaryLineNumber { get; set; }
    public LocalizationConfidence Confidence { get; set; } = LocalizationConfidence.Unknown;

    // List of other potential locations (candidates) found in stack trace
    public List<SourceLocation> CandidateLocations { get; set; } = new();
}

public class SourceLocation
{
    public string? FilePath { get; set; }
    public int? LineNumber { get; set; }
    public string? MethodName { get; set; }
}

public enum LocalizationConfidence
{
    Unknown,
    Low,      // e.g., Only method name known
    Medium,   // e.g., File known but no line number
    High      // e.g., File and Line number known
}