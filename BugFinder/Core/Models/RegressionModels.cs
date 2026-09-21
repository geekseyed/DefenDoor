namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-10: Regression Intelligence Models
/// </summary>

public class GitRepositoryInfo
{
    public string RepositoryRootPath { get; set; } = string.Empty;
    public bool IsGitRepository { get; set; }
    public string? CurrentBranch { get; set; }
    public string? CurrentCommitSha { get; set; }
    public DateTime? CommitTimestamp { get; set; }
    public string? CommitMessage { get; set; }
    public string? AuthorName { get; set; }
}

public class CommitInfo
{
    public string Sha { get; set; } = string.Empty;
    public string ShortSha { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public List<string> ChangedFiles { get; set; } = new();
}

public class RegressionAnalysisResult
{
    public string? FailingCommitSha { get; set; }
    public string? LastPassingCommitSha { get; set; }
    public List<CommitInfo> SuspectCommits { get; set; } = new();
    public List<string> ChangedFilesInScope { get; set; } = new();
    public RegressionConfidence Confidence { get; set; } = RegressionConfidence.Unknown;
    public string? AnalysisMessage { get; set; }
}

public enum RegressionConfidence
{
    Unknown,
    Low,      // Only file-level correlation
    Medium,   // Method-level correlation
    High      // Line-level + temporal correlation
}


public class RevisionSnapshot
{
    public string CommitSha { get; set; } = string.Empty;
    public string ShortSha { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string? AuthorEmail { get; set; }
    public DateTime CommitTimestamp { get; set; }
    public DateTime? CommitterTimestamp { get; set; }
    public List<string> ParentShas { get; set; } = new();

    // Dirty State
    public bool IsDirty { get; set; }
    public int FilesChangedCount { get; set; }
    public List<string> ChangedFiles { get; set; } = new();

    // Tags & Main Branch Relation
    public List<string> TagsOnThisCommit { get; set; } = new();
    public int CommitsAheadOfMain { get; set; }
    public int CommitsBehindMain { get; set; }
}

public class RevisionSnapshotResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public RevisionSnapshot? Snapshot { get; set; }
}