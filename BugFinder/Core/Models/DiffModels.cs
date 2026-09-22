namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-10.6: Git Diff Analysis Models
/// </summary>

public class GitDiffResult
{
    public string FromSha { get; set; } = string.Empty;
    public string ToSha { get; set; } = string.Empty;
    public List<DiffFile> Files { get; set; } = new();
    public int TotalFilesChanged { get; set; }
    public int TotalLinesAdded { get; set; }
    public int TotalLinesDeleted { get; set; }
}

public class DiffFile
{
    public string OldFilePath { get; set; } = string.Empty;
    public string NewFilePath { get; set; } = string.Empty;
    public FileType ChangeType { get; set; }
    public List<DiffHunk> Hunks { get; set; } = new();
    public int LinesAdded { get; set; }
    public int LinesDeleted { get; set; }
}

public enum FileType
{
    Added,
    Deleted,
    Modified,
    Renamed
}

public class DiffHunk
{
    public int OldStartLine { get; set; }
    public int OldLineCount { get; set; }
    public int NewStartLine { get; set; }
    public int NewLineCount { get; set; }
    public string Header { get; set; } = string.Empty;
    public List<DiffLine> Lines { get; set; } = new();
}

public class DiffLine
{
    public LineType Type { get; set; }
    public int OldLineNumber { get; set; } = -1;
    public int NewLineNumber { get; set; } = -1;
    public string Content { get; set; } = string.Empty;
}

public enum LineType
{
    Context,    // No change (' ')
    Addition,   // Added line ('+')
    Deletion,   // Deleted line ('-')
    Header      // Hunk header ('@')
}