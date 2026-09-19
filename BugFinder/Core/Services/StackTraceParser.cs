using System.Text.RegularExpressions;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-03.1: Stack Trace Parsing Engine
/// Parses raw stack trace strings into structured StackFrame objects.
/// </summary>
public partial class StackTraceParser
{
    // Regex pattern for standard .NET stack frames:
    // "   at Namespace.Type.Method(String args) in C:\Path\File.cs:line 42"
    [GeneratedRegex(@"^\s*at\s+(?<method>[^\(]+)\((?<args>[^\)]*)\)\s+in\s+(?<file>.+):line\s+(?<line>\d+)", RegexOptions.Multiline)]
    private static partial Regex FrameWithFileRegex();

    [GeneratedRegex(@"^\s*at\s+(?<method>[^\(]+)\((?<args>[^\)]*)\)", RegexOptions.Multiline)]
    private static partial Regex FrameNoFileRegex();

    public ParsedStackTrace Parse(string? rawTrace)
    {
        var result = new ParsedStackTrace
        {
            RawTrace = rawTrace ?? string.Empty,
            IsMalformed = string.IsNullOrWhiteSpace(rawTrace)
        };

        if (result.IsMalformed) return result;

        var lines = rawTrace!.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        int frameIndex = 0;

        foreach (var line in lines)
        {
            var frame = ParseLine(line, frameIndex++);
            if (frame != null)
            {
                frame.Kind = ClassifyFrame(frame);
                result.Frames.Add(frame);
            }
        }

        return result;
    }

    private StackFrame? ParseLine(string line, int index)
    {
        // Try matching with file/line info first
        var match = FrameWithFileRegex().Match(line);
        if (match.Success)
        {
            return CreateFrameFromMatch(match, index, line);
        }

        // Fallback to method-only match
        match = FrameNoFileRegex().Match(line);
        if (match.Success)
        {
            return CreateFrameFromMatch(match, index, line);
        }

        return null;
    }

    private StackFrame CreateFrameFromMatch(Match match, int index, string rawLine)
    {
        var methodNameFull = match.Groups["method"].Value.Trim();
        var parts = methodNameFull.Split('.');

        var frame = new StackFrame
        {
            Index = index,
            RawLine = rawLine,
            MethodName = parts.LastOrDefault(),
            TypeName = parts.Length > 1 ? parts[^2] : null,
            Namespace = parts.Length > 2 ? string.Join(".", parts.Take(parts.Length - 2)) : null
        };

        if (match.Groups["file"].Success)
            frame.FilePath = match.Groups["file"].Value;

        if (match.Groups["line"].Success && int.TryParse(match.Groups["line"].Value, out var lineNum))
            frame.LineNumber = lineNum;

        return frame;
    }

    private FrameKind ClassifyFrame(StackFrame frame)
    {
        if (string.IsNullOrEmpty(frame.Namespace)) return FrameKind.Unknown;

        if (frame.Namespace.StartsWith("ISCM."))
        {
            if (frame.Namespace.Contains("Tests")) return FrameKind.Test;
            return FrameKind.Application;
        }

        if (frame.Namespace.StartsWith("System.") || frame.Namespace.StartsWith("Microsoft."))
            return FrameKind.Framework;

        if (frame.Namespace.StartsWith("xUnit") || frame.Namespace.StartsWith("Moq") ||
            frame.Namespace.StartsWith("FluentAssertions"))
            return FrameKind.External;

        return FrameKind.External;
    }
}