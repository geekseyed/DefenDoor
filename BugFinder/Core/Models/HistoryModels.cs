using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-11.1: Failure History Models
/// Represents a persistent record of a specific failure instance.
/// </summary>
public class FailureRecord
{
    public string FailureSignature { get; set; } = string.Empty;
    public string TestName { get; set; } = string.Empty;

    // Added both for compatibility, but 'ErrorMessage' is preferred
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; }

    public DateTime OccurredAt { get; set; }
    public string? StackTrace { get; set; }
    public string? FilePath { get; set; }
    public int? LineNumber { get; set; }
    public string CommitSha { get; set; } = string.Empty;
}

/// <summary>
/// BF-11.1: Internal storage model for serialization to JSON.
/// </summary>
public class FailureHistoryStore
{
    public List<FailureRecord> Records { get; set; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// BF-11.1: Model representing the history of a specific failure signature.
/// </summary>
public class FailureHistoryResult
{
    public string FailureSignature { get; set; } = string.Empty;
    public int TotalOccurrences { get; set; }
    public DateTime? FirstSeen { get; set; }
    public DateTime? LastSeen { get; set; }
    public List<FailureRecord> RecentOccurrences { get; set; } = new();

    /// <summary>
    /// Calculated property: True if TotalOccurrences > 1.
    /// Cannot be set manually.
    /// </summary>
    public bool IsRecurring => TotalOccurrences > 1;

    /// <summary>
    /// Helper for time span calculation (Nullable).
    /// </summary>
    public TimeSpan? Duration => (FirstSeen.HasValue && LastSeen.HasValue)
        ? LastSeen.Value - FirstSeen.Value
        : null;
}