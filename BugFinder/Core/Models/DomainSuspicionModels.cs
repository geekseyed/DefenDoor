using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-12.8: Domain Failure Suspiciousness Models
/// Extends SBFL to weigh Domain Evaluation Failures higher than standard Test Failures.
/// </summary>

/// <summary>
/// Represents a domain evaluation failure linked to code coverage.
/// </summary>
public class DomainFailureSpectrum
{
    public string SubControlId { get; set; } = string.Empty; // e.g., "EVL-001.4"
    public string Severity { get; set; } = string.Empty;     // Critical, High, Medium, Low
    public List<string> CoveredLines { get; set; } = new();  // Lines executed during this failing evaluation
    public string FailureReason { get; set; } = string.Empty;
}

/// <summary>
/// Extended spectrum that includes domain-specific weighting factors.
/// </summary>
public class WeightedExecutionSpectrum : ExecutionSpectrum
{
    // Standard counts inherited from ExecutionSpectrum (PassedCount, FailedCount, etc.)

    // Domain-specific counts
    public int DomainFailureCount { get; set; }      // Number of domain evaluations failed while executing this line
    public int DomainSuccessCount { get; set; }      // Number of domain evaluations passed while executing this line

    // Calculated Weight Factor (e.g., 1.5x if a critical domain rule failed here)
    public double DomainWeightFactor { get; set; } = 1.0;

    // Final Adjusted Score after applying domain weight
    public double AdjustedSuspiciousness { get; set; }
}

/// <summary>
/// Configuration for how much to trust Domain Failures over Test Failures.
/// </summary>
public class DomainWeightConfig
{
    // Base multiplier for any domain failure
    public double BaseDomainMultiplier { get; set; } = 1.5;

    // Additional multipliers based on severity
    public double CriticalSeverityBonus { get; set; } = 1.0; // Total 2.5x
    public double HighSeverityBonus { get; set; } = 0.5;     // Total 2.0x
    public double MediumSeverityBonus { get; set; } = 0.2;   // Total 1.7x

    // If a line is covered ONLY by domain failures (and no passing tests), boost further
    public double IsolationBonus { get; set; } = 0.5;
}