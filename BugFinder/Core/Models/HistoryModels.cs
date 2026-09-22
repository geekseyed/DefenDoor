using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-11: Failure & Code History Intelligence Models
/// </summary>

/// <summary>
/// Represents a persistent record of a specific failure instance.
/// Used to build history for recurring failure detection.
/// </summary>
public class FailureRecord
{
    // Unique ID for this specific occurrence (Instance ID)
    public string RecordId { get; set; } = Guid.NewGuid().ToString("N");

    // The stable signature that identifies the TYPE of failure (from BF-02.8)
    public string FailureSignature { get; set; } = string.Empty;

    // Timestamp of occurrence
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    // Contextual Location Info (Snapshot at time of failure)
    public string? TestName { get; set; }
    public string? SourceFile { get; set; }
    public string? MethodName { get; set; }
    public int? LineNumber { get; set; }

    // Brief Summary for quick viewing
    public string? Summary { get; set; }

    // Git Commit SHA where this failure occurred (Optional, populated if available)
    public string? CommitSha { get; set; }
}

/// <summary>
/// Result of querying the failure history.
/// </summary>

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
}

/// <summary>
/// BF-11.1: Internal storage model for serialization to JSON.
/// </summary>
public class FailureHistoryStore
{
    public List<FailureRecord> Records { get; set; } = new();

    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}