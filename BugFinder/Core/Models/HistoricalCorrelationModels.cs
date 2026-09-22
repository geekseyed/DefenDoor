using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-11.5: Historical Failure Correlation Models
/// Links failure history with code change frequency to identify true problem areas.
/// </summary>

public class HistoricalCorrelationResult
{
    public string FilePath { get; set; } = string.Empty;

    // Metrics
    public int TotalChanges { get; set; }
    public int TotalFailures { get; set; }
    public double ChurnScore { get; set; }      // From BF-11.4
    public double FailureScore { get; set; }    // From BF-11.1

    // The Core Metric: How strongly are changes and failures linked?
    public double CorrelationCoefficient { get; set; } // -1.0 to 1.0

    // Uses existing enum from CorrelationModels.cs (BF-0.5)
    public CorrelationStrength Strength { get; set; }

    public List<string> AssociatedFailureSignatures { get; set; } = new();
    public DateTime LastChangeDate { get; set; }
    public DateTime LastFailureDate { get; set; }
}

public class HistoricalCorrelationReport
{
    public List<HistoricalCorrelationResult> Results { get; set; } = new();
    public int TotalFilesAnalyzed { get; set; }
    public string Summary { get; set; } = string.Empty;
}