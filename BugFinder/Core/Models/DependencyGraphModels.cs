using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-13.6: Dependency Graph Models
/// Multi-level dependency edges (Project / Type / Method) built on
/// BF-13.4 StableKeys.
/// </summary>

/// <summary>Stage 1: project-level edge (FromProject depends on ToProject).</summary>
public class ProjectDependencyEdge
{
    public string FromProject { get; set; } = string.Empty;
    public string ToProject { get; set; } = string.Empty;
}

/// <summary>How a type depends on another type.</summary>
public enum TypeDependencyKind
{
    Inheritance,    // base class / interface
    FieldType,
    PropertyType,
    ParameterType,
    ReturnType,
    Instantiation,  // new T()
    StaticAccess    // StaticMember access (e.g., Logger.Log)
}

/// <summary>Stage 2: type-level edge.</summary>
public class TypeDependencyEdge
{
    public string FromTypeKey { get; set; } = string.Empty;
    public string FromTypeName { get; set; } = string.Empty;
    public string? ToTypeKey { get; set; }          // null when unresolved
    public string? ToTypeName { get; set; }         // raw syntax when unresolved
    public TypeDependencyKind Kind { get; set; }
    public bool IsResolved { get; set; }
    public string? ContainingDocument { get; set; }
}

/// <summary>Stage 3: method → type usage edge.</summary>
public class MethodTypeDependencyEdge
{
    public string FromMethodKey { get; set; } = string.Empty;
    public string FromMethodName { get; set; } = string.Empty;
    public string? ToTypeKey { get; set; }
    public string? ToTypeName { get; set; }
    public TypeDependencyKind Kind { get; set; }    // Instantiation / StaticAccess
    public bool IsResolved { get; set; }
    public string? ContainingDocument { get; set; }
}

/// <summary>Per-document evidence container.</summary>
public class DocumentDependencyEvidence
{
    public string DocumentName { get; set; } = string.Empty;
    public List<TypeDependencyEdge> TypeEdges { get; set; } = new();
    public List<MethodTypeDependencyEdge> MethodEdges { get; set; } = new();
}

/// <summary>Stage 4: one node of the dependency graph with fan metrics.</summary>
public class DependencyNode
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty; // "Project" / "Type" / "Method"
    public int FanIn { get; set; }   // distinct sources depending on this node
    public int FanOut { get; set; }  // distinct targets this node depends on
}

/// <summary>Aggregated dependency graph report.</summary>
public class DependencyGraphReport
{
    public List<DocumentDependencyEvidence> Documents { get; set; } = new();
    public List<ProjectDependencyEdge> ProjectEdges { get; set; } = new();
    public List<TypeDependencyEdge> TypeEdges { get; set; } = new();
    public List<MethodTypeDependencyEdge> MethodEdges { get; set; } = new();
    public List<DependencyNode> Nodes { get; set; } = new();

    public int TotalProjects { get; set; }
    public int TotalProjectDependencies { get; set; }
    public int TotalTypeEdges { get; set; }
    public int TotalMethodEdges { get; set; }
    public int TotalUnresolvedTypeEdges { get; set; }
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}