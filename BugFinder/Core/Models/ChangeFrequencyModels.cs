using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-11.4: Change Frequency Models
/// Tracks how often code locations change and correlates with failures.
/// </summary>

public class FileChangeHistory
{
    public string FilePath { get; set; } = string.Empty;
    public int TotalCommits { get; set; }
    public int TotalLinesAdded { get; set; }
    public int TotalLinesDeleted { get; set; }
    public DateTime FirstChange { get; set; }
    public DateTime LastChange { get; set; }

    // List of commit shas that touched this file
    public List<string> RelatedCommitShas { get; set; } = new();

    // Calculated: Is this file a hotspot?
    public bool IsHotspot => TotalCommits > 10; // Threshold configurable
}

public class ChangeFrequencyResult
{
    public string FilePath { get; set; } = string.Empty;
    public int FailureCount { get; set; }
    public int ChangeCount { get; set; }

    // Correlation score: High if both failure count and change count are high
    public double ChurnFailureScore { get; set; } // 0.0 to 1.0

    public List<FailureRecord> AssociatedFailures { get; set; } = new();
    public List<string> RecentCommitShas { get; set; } = new();
}

public class HotspotReport
{
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public List<FileChangeHistory> TopChangedFiles { get; set; } = new();
    public List<ChangeFrequencyResult> TopFailureProneFiles { get; set; } = new();

    public string Summary { get; set; } = string.Empty;
}