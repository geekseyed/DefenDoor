using ISCM.BugFinder.Core.Models;
using ISCM.Domain.Enums;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-04: Evidence Engine
/// Represents a complete evidence package for a single failure.
/// </summary>
public class EvidencePackage
{
    public string FailureId { get; set; } = string.Empty;
    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;

    // Core Evidence Items
    public List<EvidenceItem> Items { get; set; } = new();

    // Metadata
    public string? SourceTestId { get; set; }
    public string? SubControlId { get; set; }
    public FailureType FailureType { get; set; }
}

/// <summary>
/// A single piece of evidence within a package.
/// </summary>
public class EvidenceItem
{
    public EvidenceType Type { get; set; }
    public string Category { get; set; } = string.Empty; // e.g., "Assertion", "Exception", "Domain"
    public string Content { get; set; } = string.Empty;
    public string? RawData { get; set; } // Original unprocessed data if available
    public EvidenceProvenance Provenance { get; set; } = new();
}

/// <summary>
/// Tracks where and how the evidence was obtained.
/// </summary>
public class EvidenceProvenance
{
    public string Source { get; set; } = string.Empty; // e.g., "TRX", "Console", "CoverageReport"
    public DateTime Timestamp { get; set; }
    public string? ExtractedBy { get; set; } // Name of the parser/service
}

public enum EvidenceType
{
    Unknown,
    TestExecution,      // Added for BF-04.1
    Exception,
    DomainEvaluation,
    SourceLocation,     // Added for BF-04.1
    ErrorMessage,       // Added for BF-04.1
    Coverage,
    Runtime,
    Configuration,
    GitHistory
}