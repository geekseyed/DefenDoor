using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-13.1: Roslyn Integration Models
/// Snapshots produced while integrating the Roslyn workspace.
/// </summary>

/// <summary>Stage 1: kind of workspace Roslyn is running in.</summary>
public enum RoslynWorkspaceKind
{
    Adhoc,    // in-memory, fast (unit tests / lightweight parsing)
    MSBuild   // real .sln / .csproj loading
}

/// <summary>Stage 1-3: workspace + project inventory.</summary>
public class RoslynWorkspaceSnapshot
{
    public RoslynWorkspaceKind Kind { get; set; }
    public string? SolutionPath { get; set; }
    public string? SolutionName { get; set; }
    public int ProjectCount { get; set; }
    public List<RoslynProjectSnapshot> Projects { get; set; } = new();
    public DateTime LoadedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Stage 3: one project inside the loaded solution.</summary>
public class RoslynProjectSnapshot
{
    public string Name { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public string Language { get; set; } = string.Empty;
    public int DocumentCount { get; set; }
    public List<string> DocumentNames { get; set; } = new();
}

/// <summary>Stage 4: compilation-level facts for one project.</summary>
public class RoslynCompilationSnapshot
{
    public string AssemblyName { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public int SyntaxTreeCount { get; set; }
    public int MetadataReferenceCount { get; set; }
}