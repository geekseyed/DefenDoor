using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-13.3: Semantic Model Models
/// Type/symbol resolution evidence. Models are Roslyn-free by design
/// (Roslyn types stay inside Services).
/// </summary>

public enum SemanticSymbolKind
{
    Type, Method, Property, Field, Parameter, Local, Unknown
}

/// <summary>Stage 2: type of an expression's result.</summary>
public class SemanticTypeInfo
{
    public string Expression { get; set; } = string.Empty;
    public string? TypeName { get; set; }
    public string? FullTypeName { get; set; }
    public string? Namespace { get; set; }
    public string? Kind { get; set; }          // Class / Struct / Enum / ...
    public bool IsResolved { get; set; }
}

/// <summary>Stage 3: a resolved (or unresolved) invoked symbol.</summary>
public class SemanticSymbolEvidence
{
    public string SyntaxIdentifier { get; set; } = string.Empty;
    public SemanticSymbolKind Kind { get; set; } = SemanticSymbolKind.Unknown;
    public bool IsResolved { get; set; }
    public string? DisplayName { get; set; }        // e.g. System.Console.WriteLine(string)
    public string? ContainingTypeName { get; set; } // e.g. Console
    public string? ContainingNamespace { get; set; }// e.g. System
    public string? Accessibility { get; set; }      // Public / Private / ...
    public bool IsStatic { get; set; }
}

/// <summary>Stage 3: a method declared in the analyzed document.</summary>
public class MethodSemanticEvidence
{
    public string MethodName { get; set; } = string.Empty;
    public string? FullSignature { get; set; }   // e.g. Sample.Calculator.Add(int, int): int
    public string? ReturnType { get; set; }
    public int ParameterCount { get; set; }
    public List<string> ParameterTypes { get; set; } = new();
    public bool IsStatic { get; set; }
}

/// <summary>All semantic evidence for one document.</summary>
public class DocumentSemanticEvidence
{
    public string DocumentName { get; set; } = string.Empty;
    public List<MethodSemanticEvidence> DeclaredMethods { get; set; } = new();
    public List<SemanticSymbolEvidence> InvokedSymbols { get; set; } = new();
    public List<SemanticTypeInfo> ExpressionTypes { get; set; } = new();
    public int UnresolvedCount { get; set; }
}

/// <summary>Stage 4: aggregated semantic report.</summary>
public class SemanticAnalysisReport
{
    public List<DocumentSemanticEvidence> Documents { get; set; } = new();
    public int TotalDocuments { get; set; }
    public int TotalMethods { get; set; }
    public int TotalResolvedSymbols { get; set; }
    public int TotalUnresolvedSymbols { get; set; }
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}