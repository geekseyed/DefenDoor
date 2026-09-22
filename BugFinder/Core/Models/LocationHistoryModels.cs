using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-11.2: Location History Models
/// Tracks the history of code locations associated with failures.
/// </summary>

public class CodeLocationRecord
{
    public string FilePath { get; set; } = string.Empty;
    public string? MethodName { get; set; }
    public int? LineNumber { get; set; }
    public string FailureSignature { get; set; } = string.Empty;
    public DateTime RecordedAt { get; set; }
    public string CommitSha { get; set; } = string.Empty; // Optional: Link to specific commit
}

public class LocationHistoryResult
{
    public string FilePath { get; set; } = string.Empty;
    public string? MethodName { get; set; }

    public int TotalFailuresAtLocation { get; set; }
    public List<CodeLocationRecord> RecentRecords { get; set; } = new();

    public DateTime FirstFailure { get; set; }
    public DateTime LastFailure { get; set; }

    // Computed: Is this location a known hotspot?
    public bool IsHotspot => TotalFailuresAtLocation >= 3; // Threshold can be configurable

    public List<string> AssociatedFailureSignatures { get; set; } = new();
}