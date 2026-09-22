using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-0.5 & BF-10.7: Execution Path Correlation & Regression Analysis Models
/// </summary>

// ==========================================
// BF-0.5: Execution Path & Cross-Test Correlation
// ==========================================

public class CorrelationResult
{
    public int TotalFailuresAnalyzed { get; set; }
    public List<FailureCluster> Clusters { get; set; } = new();
    public List<string> IsolatedFailures { get; set; } = new();
}

public class FailureCluster
{
    public CorrelationStrength CorrelationStrength { get; set; }
    public SharedCodeElement SharedElement { get; set; } = new();
    public List<string> RelatedFailureIds { get; set; } = new();
    public int CommonStackTraceDepth { get; set; }
}

public class SharedCodeElement
{
    public string ElementType { get; set; } = string.Empty; // e.g., "Method", "File", "Class"
    public string Name { get; set; } = string.Empty;
}

public enum CorrelationStrength
{
    Unknown,
    None,       // Added for BF-11.5 (No link)
    Weak,       // e.g., Same Assembly
    Medium,     // e.g., Same File or Class (Required fix)
    Moderate,   // Added for BF-11.5 (Some link)
    Strong,     // e.g., Same Method or Line
    Critical    // Added for BF-11.5 (Extremely high churn + failure)
}

// ==========================================
// BF-10.7: Regression Correlation (Changes vs Failures)
// ==========================================

public class RegressionCorrelationResult
{
    public string FromSha { get; set; } = string.Empty;
    public string ToSha { get; set; } = string.Empty;

    // Inputs
    public List<string> FailingTestNames { get; set; } = new();
    public List<CommitInfo> CommitsInRange { get; set; } = new();

    // Output: Ranked list of suspicious files
    public List<SuspectFile> SuspectFiles { get; set; } = new();

    public AnalysisStrategy Strategy { get; set; }
}

public class SuspectFile
{
    public string FilePath { get; set; } = string.Empty;
    public int ChangeFrequency { get; set; } // How many commits touched this file
    public List<string> RelatedCommits { get; set; } = new();
    public double SuspicionScore { get; set; } // 0.0 to 1.0

    // Heuristic metadata
    public bool IsTestFile { get; set; }
    public string? FileExtension { get; set; }
}

public enum AnalysisStrategy
{
    FrequencyBased,      // Files changed most often are most suspicious
    RecencyWeighted,     // Recent changes weigh more
    Hybrid               // Combination (Default)
}