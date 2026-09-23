using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-13.7: Static Code Relationship Models
/// Normalized four-kind taxonomy over static code structure,
/// built on BF-13.4 StableKeys by unifying BF-13.5 + BF-13.6 outputs.
/// </summary>

public enum StaticRelationshipKind
{
    Inheritance,   // is-a
    Composition,   // has-a (field / property ownership)
    Invocation,    // method → method call
    Uses           // weaker usage: parameter / return / instantiation / static access
}

public class StaticRelationship
{
    public StaticRelationshipKind Kind { get; set; }
    public string FromKey { get; set; } = string.Empty;
    public string FromDisplayName { get; set; } = string.Empty;
    public string? ToKey { get; set; }              // null when unresolved
    public string? ToDisplayName { get; set; }      // raw syntax when unresolved
    public bool IsResolved { get; set; }

    /// <summary>Provenance: the finer-grained source edge kind (e.g. FieldType, StaticAccess, call).</summary>
    public string? Detail { get; set; }
    public string? ContainingDocument { get; set; }
}

public class StaticRelationshipReport
{
    public List<StaticRelationship> Relationships { get; set; } = new();

    public List<StaticRelationship> Inheritance { get; set; } = new();
    public List<StaticRelationship> Composition { get; set; } = new();
    public List<StaticRelationship> Invocations { get; set; } = new();
    public List<StaticRelationship> Uses { get; set; } = new();

    public int TotalRelationships { get; set; }
    public int TotalInheritance { get; set; }
    public int TotalComposition { get; set; }
    public int TotalInvocations { get; set; }
    public int TotalUses { get; set; }
    public int TotalUnresolved { get; set; }

    public List<string> UniqueSymbolKeys { get; set; } = new();
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}