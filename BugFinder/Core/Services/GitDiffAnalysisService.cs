using System.Diagnostics;
using System.Text.RegularExpressions;
using ISCM.BugFinder.Core.Models;
using Microsoft.VisualBasic.FileIO;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-10.6: Git Diff Analysis Service
/// Parses git diff output to extract detailed line-by-line changes
/// </summary>
public class GitDiffAnalysisService
{
    private readonly string? _repositoryRootPath;

    public GitDiffAnalysisService(string? workingDirectory = null)
    {
        // Try to find repo root if workingDirectory is not provided or not root
        if (string.IsNullOrEmpty(workingDirectory))
        {
            workingDirectory = Directory.GetCurrentDirectory();
        }

        _repositoryRootPath = FindGitRepositoryRoot(workingDirectory);
    }

    /// <summary>
    /// BF-10.6 - Stage 1: Extract diff between two revisions
    /// </summary>
    public GitDiffResult AnalyzeDiff(string fromSha, string toSha)
    {
        if (string.IsNullOrEmpty(_repositoryRootPath))
        {
            throw new InvalidOperationException("Not a Git repository");
        }

        var result = new GitDiffResult
        {
            FromSha = fromSha,
            ToSha = toSha
        };

        try
        {
            // Execute git diff with unified format and full index
            // -U999999 ensures we get all context lines for accurate parsing
            var diffOutput = ExecuteGitCommand("diff", $"--full-index -U999999 {fromSha}..{toSha}");

            if (string.IsNullOrWhiteSpace(diffOutput))
            {
                return result; // No changes
            }

            result.Files = ParseDiffOutput(diffOutput);
            result.TotalFilesChanged = result.Files.Count;

            // Calculate totals
            foreach (var file in result.Files)
            {
                result.TotalLinesAdded += file.LinesAdded;
                result.TotalLinesDeleted += file.LinesDeleted;
            }

            return result;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to analyze diff: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// BF-10.6 - Stage 2: Parse raw diff output into structured objects
    /// </summary>
    private List<DiffFile> ParseDiffOutput(string diffOutput)
    {
        var files = new List<DiffFile>();
        var lines = diffOutput.Split('\n');

        var currentFile = new DiffFile();
        var currentHunk = new DiffHunk();
        var inHunk = false;
        bool fileInitialized = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Detect new file section
            if (line.StartsWith("diff --git "))
            {
                if (inHunk && currentHunk.Lines.Any())
                {
                    currentFile.Hunks.Add(currentHunk);
                }

                if (fileInitialized)
                {
                    files.Add(currentFile);
                }

                currentFile = new DiffFile();
                currentHunk = new DiffHunk();
                inHunk = false;
                fileInitialized = true;

                // Extract file paths from diff line
                // Format: diff --git a/path/to/file b/path/to/file
                var parts = line.Split(' ', 4);
                if (parts.Length >= 4)
                {
                    var oldPathPart = parts[2]; // a/path...
                    var newPathPart = parts[3]; // b/path...

                    // Handle spaces in filenames by taking everything after 'a/' and 'b/'
                    if (oldPathPart.StartsWith("a/")) currentFile.OldFilePath = oldPathPart.Substring(2);
                    else currentFile.OldFilePath = oldPathPart;

                    if (newPathPart.StartsWith("b/")) currentFile.NewFilePath = newPathPart.Substring(2);
                    else currentFile.NewFilePath = newPathPart;
                }
            }
            // Detect file mode changes or other metadata
            else if (line.StartsWith("new file"))
            {
                currentFile.ChangeType = FileType.Added;
            }
            else if (line.StartsWith("deleted file"))
            {
                currentFile.ChangeType = FileType.Deleted;
            }
            else if (line.StartsWith("rename from"))
            {
                currentFile.ChangeType = FileType.Renamed;
            }
            // Detect hunk header
            else if (line.StartsWith("@@ "))
            {
                if (inHunk && currentHunk.Lines.Any())
                {
                    currentFile.Hunks.Add(currentHunk);
                }

                currentHunk = ParseHunkHeader(line);
                inHunk = true;
            }
            // Process hunk content
            else if (inHunk)
            {
                var diffLine = ParseDiffLine(line);
                if (diffLine != null)
                {
                    currentHunk.Lines.Add(diffLine);

                    // Update file stats based on line type
                    switch (diffLine.Type)
                    {
                        case LineType.Addition:
                            currentFile.LinesAdded++;
                            break;
                        case LineType.Deletion:
                            currentFile.LinesDeleted++;
                            break;
                    }
                }
            }
        }

        // Add last file if it has content
        if (inHunk && currentHunk.Lines.Any())
        {
            currentFile.Hunks.Add(currentHunk);
        }

        if (fileInitialized && !string.IsNullOrEmpty(currentFile.NewFilePath))
        {
            files.Add(currentFile);
        }

        return files;
    }

    /// <summary>
    /// Parse hunk header like "@@ -1,5 +1,6 @@"
    /// </summary>
    private DiffHunk ParseHunkHeader(string header)
    {
        var hunk = new DiffHunk { Header = header };

        // Regex to parse hunk header: @@ -old_start,old_count +new_start,new_count @@
        var regex = new Regex(@"@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@");
        var match = regex.Match(header);

        if (match.Success)
        {
            hunk.OldStartLine = int.Parse(match.Groups[1].Value);
            hunk.OldLineCount = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 1;
            hunk.NewStartLine = int.Parse(match.Groups[3].Value);
            hunk.NewLineCount = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 1;
        }

        return hunk;
    }

    /// <summary>
    /// Parse individual diff line
    /// </summary>
    private DiffLine? ParseDiffLine(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return new DiffLine { Type = LineType.Context, Content = line };
        }

        var firstChar = line[0];
        var content = line.Length > 0 ? line.Substring(1) : string.Empty; // Remove the indicator character

        var diffLine = new DiffLine { Content = content };

        switch (firstChar)
        {
            case ' ':
                diffLine.Type = LineType.Context;
                break;
            case '+':
                diffLine.Type = LineType.Addition;
                break;
            case '-':
                diffLine.Type = LineType.Deletion;
                break;
            case '\\':
                // Ignore "\ No newline at end of file"
                return null;
            default:
                // Ignore other types
                return null;
        }

        return diffLine;
    }

    /// <summary>
    /// Helper: Execute git command
    /// </summary>
    private string ExecuteGitCommand(string command, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"{command} {arguments}",
            WorkingDirectory = _repositoryRootPath ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0 && !string.IsNullOrEmpty(error))
        {
            throw new InvalidOperationException($"Git command failed: {error}");
        }

        return output.Trim();
    }

    /// <summary>
    /// Helper: Find Git Repository Root
    /// </summary>
    private static string? FindGitRepositoryRoot(string startingDirectory)
    {
        var directory = new DirectoryInfo(startingDirectory);
        while (directory != null)
        {
            var gitDirectory = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitDirectory) || File.Exists(gitDirectory))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        return null;
    }
}