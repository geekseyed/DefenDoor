namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-03.1 & BF-10.8: Stack Trace Parsing & Regression Localization Models
/// </summary>

// ==========================================
// BF-03.1: Stack Trace Parsing Engine (Existing)
// ==========================================

public class StackFrame
{
    public int Index { get; set; }
    public string? Namespace { get; set; }
    public string? TypeName { get; set; }
    public string? MethodName { get; set; }
    public string? FilePath { get; set; }
    public int? LineNumber { get; set; }
    public int? ColumnNumber { get; set; }
    public FrameKind Kind { get; set; } = FrameKind.Unknown;
    public string? RawLine { get; set; }
}

public enum FrameKind
{
    Unknown,
    Application,
    Test,
    Framework,
    External
}

public class ParsedStackTrace
{
    public List<StackFrame> Frames { get; set; } = new();
    public string? ExceptionType { get; set; }
    public string? ExceptionMessage { get; set; }
    public bool IsMalformed { get; set; }
    public string? RawTrace { get; set; }
}

// ==========================================
// BF-10.8: Regression Localization (Updated to match Service)
// ==========================================

/// <summary>
/// BF-10.8: Final localized regression report combining all analysis phases.
/// Matches the structure expected by RegressionLocalizationService.
/// </summary>
public class RegressionLocalizationReport

{

    public string RegressionId { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8); // <-- ADD THIS

    // Input Range
    public string LastPassingSha { get; set; } = string.Empty;
    public string FirstFailingSha { get; set; } = string.Empty;

    // Statistics (Required by Service)
    public int TotalFailingTests { get; set; }
    public int TotalCommitsAnalyzed { get; set; }
    public int TotalFilesChanged { get; set; }

    // Analysis Results
    public List<SuspectFile> RankedSuspects { get; set; } = new();
    public List<CommitInfo> CommitsInScope { get; set; } = new();
    public List<string> FailingTests { get; set; } = new();

    // List of Hypotheses (Required by Service)
    public List<RootCauseHypothesis> Hypotheses { get; set; } = new();

    // The "Smoking Gun": Most likely root cause (Single best guess)
    public LocalizedRootCause? RootCause { get; set; }

    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public LocalizationStrategy Strategy { get; set; }
}

/// <summary>
/// Represents a single hypothesis for the root cause (one per suspect file).
/// Matches the structure used in RegressionLocalizationService.
/// </summary>
public class RootCauseHypothesis
{
    public int Rank { get; set; }
    public SuspectFile SuspectFile { get; set; } = new();
    public List<string> RelatedFailingTests { get; set; } = new();
    public List<DiffHunk> SuspiciousHunks { get; set; } = new();

    public double ConfidenceScore { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public string SuggestedAction { get; set; } = string.Empty;
}

/// <summary>
/// Represents the single most likely root cause of the regression (Summary).
/// </summary>
public class LocalizedRootCause
{
    public string FilePath { get; set; } = string.Empty;
    public string CommitSha { get; set; } = string.Empty;
    public string CommitMessage { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;

    public string? MethodName { get; set; }
    public int? LineNumber { get; set; }

    public double Confidence { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public List<string> SuggestedActions { get; set; } = new();
}

public enum LocalizationStrategy
{
    StackTraceDriven,
    ChangeFrequencyDriven,
    Hybrid
}