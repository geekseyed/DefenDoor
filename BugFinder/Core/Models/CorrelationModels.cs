namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-05: Execution Path & Cross-Test Correlation Models
/// </summary>
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
    Weak,      // e.g., Same Assembly
    Medium,    // e.g., Same File or Class
    Strong     // e.g., Same Method or Line
}