using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-13.2: Syntax Analysis Models
/// Syntax-level evidence built directly from Roslyn syntax trees.
/// NOTE: semantic resolution (symbols, types) belongs to BF-13.3.
/// </summary>

/// <summary>Stage 3: one method with its body facts.</summary>
public class MethodSyntaxEvidence
{
    public string MethodName { get; set; } = string.Empty;
    public string ContainingTypeName { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public int ParameterCount { get; set; }
    public int StatementCount { get; set; }
    public bool HasExecutableBody { get; set; }
    public bool IsExpressionBodied { get; set; }
}

/// <summary>Stages 1-4: evidence for one syntax tree.</summary>
public class SyntaxTreeEvidence
{
    public string? FilePath { get; set; }
    public int TotalNodeCount { get; set; }
    public int TypeCount { get; set; }
    public int MethodCount { get; set; }
    public List<MethodSyntaxEvidence> Methods { get; set; } = new();
}

/// <summary>Stage 4: aggregated syntax evidence report.</summary>
public class SyntaxAnalysisReport
{
    public List<SyntaxTreeEvidence> Trees { get; set; } = new();
    public int TotalTrees { get; set; }
    public int TotalMethods { get; set; }
    public int TotalTypes { get; set; }
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}