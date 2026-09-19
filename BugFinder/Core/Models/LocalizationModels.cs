namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// Represents a single frame in a parsed stack trace.
/// BF-03.1: Stack Trace Parsing Engine
/// </summary>
public class StackFrame
{
    public int Index { get; set; }
    public string? Namespace { get; set; }
    public string? TypeName { get; set; }
    public string? MethodName { get; set; }
    public string? FilePath { get; set; }
    public int? LineNumber { get; set; }
    public int? ColumnNumber { get; set; }
    public FrameKind Kind { get; set; } = FrameKind.Unknown;

    // Raw line from the trace for debugging/re-parsing
    public string? RawLine { get; set; }
}

public enum FrameKind
{
    Unknown,
    Application,   // Code in ISCM.* assemblies
    Test,          // Code in ISCM.Tests or test frameworks
    Framework,     // .NET Framework/Core libraries (System.*, Microsoft.*)
    External       // Third-party libraries (xUnit, Moq, etc.)
}

/// <summary>
/// Container for a fully parsed stack trace.
/// </summary>
public class ParsedStackTrace
{
    public List<StackFrame> Frames { get; set; } = new();
    public string? ExceptionType { get; set; }
    public string? ExceptionMessage { get; set; }
    public bool IsMalformed { get; set; }
    public string? RawTrace { get; set; }
}