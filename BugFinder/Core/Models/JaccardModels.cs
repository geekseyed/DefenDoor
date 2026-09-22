using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-12.6: Jaccard Algorithm Models
/// </summary>

public class JaccardResult
{
    public string ElementId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int? LineNumber { get; set; }

    public double JaccardScore { get; set; }
    public int Rank { get; set; }

    // Debugging Metadata
    public int ExecutedByFailed { get; set; }
    public int ExecutedByPassed { get; set; }
    public int TotalFailedTests { get; set; }
}

public class JaccardReport
{
    public List<JaccardResult> RankedResults { get; set; } = new();
    public int TotalElements { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

public class JaccardConfig
{
    public double MinimumScoreThreshold { get; set; } = 0.0; // Filter low scores
    public int TopNResults { get; set; } = 0; // 0 = Return all
}