using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-13.5: Caller / Callee Analysis Models
/// Static call-graph edges built on top of BF-13.4 StableKeys.
/// </summary>

/// <summary>Stage 3: one call relationship (edge).</summary>
public class CallEdge
{
    public string? CallerKey { get; set; }          // StableKey (null if caller unresolved)
    public string CallerDisplayName { get; set; } = string.Empty;
    public string? CalleeKey { get; set; }          // StableKey (null = UNKNOWN callee)
    public string CalleeDisplayName { get; set; } = string.Empty; // raw syntax when unresolved
    public bool IsResolved { get; set; }
    public string? ContainingDocument { get; set; }
}

/// <summary>Stage 4: fan-in / fan-out metrics per symbol.</summary>
public class SymbolCallMetrics
{
    public string SymbolKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>How many distinct symbols call this one.</summary>
    public int FanIn { get; set; }
    /// <summary>How many distinct symbols this one calls.</summary>
    public int FanOut { get; set; }

    public List<string> Callers { get; set; } = new();
    public List<string> Callees { get; set; } = new();
}

/// <summary>Call-graph evidence for one document.</summary>
public class DocumentCallEvidence
{
    public string DocumentName { get; set; } = string.Empty;
    public int DeclaredMethodCount { get; set; }
    public List<CallEdge> Edges { get; set; } = new();
    public int ResolvedEdgeCount { get; set; }
    public int UnresolvedEdgeCount { get; set; }
}

/// <summary>Stage 4: aggregated call-graph report.</summary>
public class CallerCalleeReport
{
    public List<DocumentCallEvidence> Documents { get; set; } = new();
    public List<CallEdge> AllEdges { get; set; } = new();
    public List<SymbolCallMetrics> Metrics { get; set; } = new();

    public int TotalDeclaredMethods { get; set; }
    public int TotalEdges { get; set; }
    public int TotalResolvedEdges { get; set; }
    public int TotalUnresolvedEdges { get; set; }
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}