using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-12: Fault Localization Models
/// Represents the output of Spectrum-Based Fault Localization (SBFL) algorithms.
/// </summary>

/// <summary>
/// Represents the "Spectrum" data for a single code element (Method/Line).
/// This is the raw input for SBFL algorithms.
/// </summary>
public class ExecutionSpectrum
{
    public string ElementId { get; set; } = string.Empty; // e.g., "File:Method:Line"
    public string FilePath { get; set; } = string.Empty;
    public string? MethodName { get; set; }
    public int? LineNumber { get; set; }

    // The 4 Counters required for SBFL
    public int PassedCount { get; set; }      // Tests that passed AND executed this line
    public int FailedCount { get; set; }      // Tests that failed AND executed this line
    public int PassSkipCount { get; set; }    // Tests that passed BUT did NOT execute this line
    public int FailSkipCount { get; set; }    // Tests that failed BUT did NOT execute this line
}

/// <summary>
/// The result of calculating suspiciousness for a single element.
/// </summary>
public class SuspiciousLocation
{
    public string ElementId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? MethodName { get; set; }
    public int? LineNumber { get; set; }

    // Scores from different algorithms
    public double OchiaiScore { get; set; }
    public double TarantulaScore { get; set; }
    public double JaccardScore { get; set; }

    // The Final Aggregated Score used for ranking
    public double FinalSuspiciousness { get; set; }

    // Rank in the final list (1 = most suspicious)
    public int Rank { get; set; }

    // Evidence backing this score
    public int ExecutingFailedTests { get; set; }
    public int ExecutingPassedTests { get; set; }
}

/// <summary>
/// Container for the full localization report.
/// </summary>
public class FaultLocalizationReport
{
    public List<SuspiciousLocation> RankedLocations { get; set; } = new();
    public SbflAlgorithm PrimaryAlgorithm { get; set; }
    public int TotalElementsAnalyzed { get; set; }
    public int TotalFailedTests { get; set; }
    public int TotalPassedTests { get; set; }

    // Important Metadata
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public string WarningMessage { get; set; } = string.Empty; // e.g., "No failed tests found"
}

public enum SbflAlgorithm
{
    Ochiai,     // Generally considered the most effective
    Tarantula,
    Jaccard,
    Hybrid      // Weighted average of all three
}