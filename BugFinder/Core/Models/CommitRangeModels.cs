namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-10.5: Commit Range Analysis Models
/// </summary>

public class CommitRangeAnalysis
{
    public string FromSha { get; set; } = string.Empty;
    public string ToSha { get; set; } = string.Empty;
    public int TotalCommits { get; set; }
    public List<CommitInfo> Commits { get; set; } = new();
    public List<string> AllChangedFiles { get; set; } = new();
    public Dictionary<string, int> FileChangeFrequency { get; set; } = new();
    public TimeSpan TimeSpan { get; set; }

    // اضافه شده برای سازگاری با سرویس
    public ChangeSetSummary ChangeSummary { get; set; } = new();
}

public class ChangeSetSummary
{
    public int TotalFilesChanged { get; set; }
    public int TotalLinesAdded { get; set; }
    public int TotalLinesDeleted { get; set; }
    public List<FileChangeStats> FileStats { get; set; } = new();

    // لیست‌های کمکی برای سازگاری با کدهای دیگر
    public List<string> AddedFiles { get; set; } = new();
    public List<string> DeletedFiles { get; set; } = new();
    public List<string> ModifiedFiles { get; set; } = new();
}

public class FileChangeStats
{
    public string FilePath { get; set; } = string.Empty;
    public int ChangeCount { get; set; }
    public int LinesAdded { get; set; }
    public int LinesDeleted { get; set; }
    public List<string> CommitShas { get; set; } = new();
}