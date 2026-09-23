using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-13.4: Symbol Resolution Models
/// Stable, deterministic symbol identities — the join key between
/// static code structure (BF-13) and dynamic evidence (BF-03/06).
/// Reuses SemanticSymbolKind from BF-13.3 (no duplicate enums).
/// </summary>

public class SymbolIdentity
{
    public SemanticSymbolKind Kind { get; set; } = SemanticSymbolKind.Unknown;
    public string AssemblyName { get; set; } = string.Empty;
    public string FullyQualifiedName { get; set; } = string.Empty;
    public string? Parameters { get; set; }   // "(int,int)" / "[string]" / null
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Deterministic, comparable key: {KindCode}|{Assembly}|{FQN}{Parameters}
    /// Same symbol resolved from different syntax locations → same key.
    /// </summary>
    public string ToStableKey()
    {
        var code = Kind switch
        {
            SemanticSymbolKind.Type => "T",
            SemanticSymbolKind.Method => "M",
            SemanticSymbolKind.Property => "P",
            SemanticSymbolKind.Field => "F",
            _ => "S"
        };
        return $"{code}|{AssemblyName}|{FullyQualifiedName}{Parameters ?? string.Empty}";
    }
}

/// <summary>One resolution attempt (declaration or call site).</summary>
public class ResolvedSymbolEvidence
{
    public string SyntaxIdentifier { get; set; } = string.Empty;
    public SemanticSymbolKind Kind { get; set; } = SemanticSymbolKind.Unknown;
    public bool IsResolved { get; set; }
    public SymbolIdentity? Identity { get; set; }   // null when unresolved
    public string? Accessibility { get; set; }
    public bool IsStatic { get; set; }
    public string? ContainingTypeName { get; set; }
    public string? ContainingNamespace { get; set; }
}

/// <summary>All resolution evidence for one document.</summary>
public class DocumentSymbolEvidence
{
    public string DocumentName { get; set; } = string.Empty;
    public List<ResolvedSymbolEvidence> Types { get; set; } = new();       // Stage 1
    public List<ResolvedSymbolEvidence> Methods { get; set; } = new();     // Stage 2 (declared)
    public List<ResolvedSymbolEvidence> Properties { get; set; } = new();  // Stage 3 (declared)
    public List<ResolvedSymbolEvidence> Invocations { get; set; } = new(); // call sites (hook for 13.5)
    public int UnresolvedCount { get; set; }
}

/// <summary>Aggregated report + unique symbol registry.</summary>
public class SymbolResolutionReport
{
    public List<DocumentSymbolEvidence> Documents { get; set; } = new();
    public int TotalDocuments { get; set; }
    public int TotalTypes { get; set; }
    public int TotalDeclaredMethods { get; set; }
    public int TotalDeclaredProperties { get; set; }
    public int TotalInvocations { get; set; }
    public int TotalResolved { get; set; }
    public int TotalUnresolved { get; set; }

    /// <summary>Distinct stable keys — foundation for BF-13.5/13.6.</summary>
    public List<string> UniqueSymbolKeys { get; set; } = new();
}