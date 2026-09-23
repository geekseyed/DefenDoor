using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ISCM.BugFinder.Core.Models;
using Microsoft.CodeAnalysis;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-13.7: Static Code Relationship Service
/// Stage 1: Symbol Relationship — normalize any 13.5/13.6 edge into a
///          StaticRelationship keyed by StableKeys
/// Stage 2: Inheritance (is-a) extraction
/// Stage 3: Composition (has-a: field/property) extraction
/// Stage 4: Invocation (method → method) extraction from call graph
/// Stage 5: Static Relationship Evidence — aggregated report
/// Principle: unresolved reference = UNKNOWN relationship, never failure.
/// </summary>
public class StaticRelationshipService
{
    private readonly DependencyGraphService _dependencies = new();
    private readonly CallerCalleeAnalysisService _calls = new();

    // Stage 1 — normalize a type-dependency edge (13.6)
    public StaticRelationship FromTypeEdge(TypeDependencyEdge edge)
    {
        if (edge is null) throw new ArgumentNullException(nameof(edge));

        var kind = edge.Kind switch
        {
            TypeDependencyKind.Inheritance => StaticRelationshipKind.Inheritance,
            TypeDependencyKind.FieldType or TypeDependencyKind.PropertyType
                => StaticRelationshipKind.Composition,
            _ => StaticRelationshipKind.Uses
        };

        return new StaticRelationship
        {
            Kind = kind,
            FromKey = edge.FromTypeKey,
            FromDisplayName = edge.FromTypeName,
            ToKey = edge.ToTypeKey,
            ToDisplayName = edge.ToTypeName,
            IsResolved = edge.IsResolved,
            Detail = edge.Kind.ToString(),
            ContainingDocument = edge.ContainingDocument
        };
    }

    // Stage 1 — normalize a call edge (13.5)
    public StaticRelationship FromCallEdge(CallEdge edge)
    {
        if (edge is null) throw new ArgumentNullException(nameof(edge));

        return new StaticRelationship
        {
            Kind = StaticRelationshipKind.Invocation,
            FromKey = edge.CallerKey ?? $"UNKNOWN|{edge.CallerDisplayName}",
            FromDisplayName = edge.CallerDisplayName,
            ToKey = edge.CalleeKey,
            ToDisplayName = edge.CalleeDisplayName,
            IsResolved = edge.IsResolved,
            Detail = "call",
            ContainingDocument = edge.ContainingDocument
        };
    }

    // Stage 2 — is-a
    public static List<StaticRelationship> ExtractInheritance(IEnumerable<StaticRelationship> relationships) =>
        relationships.Where(r => r.Kind == StaticRelationshipKind.Inheritance).ToList();

    // Stage 3 — has-a
    public static List<StaticRelationship> ExtractComposition(IEnumerable<StaticRelationship> relationships) =>
        relationships.Where(r => r.Kind == StaticRelationshipKind.Composition).ToList();

    // Stage 4 — calls
    public static List<StaticRelationship> ExtractInvocation(IEnumerable<StaticRelationship> relationships) =>
        relationships.Where(r => r.Kind == StaticRelationshipKind.Invocation).ToList();

    // Stage 5 — full project pass
    public async Task<StaticRelationshipReport> AnalyzeProjectAsync(Project project, CancellationToken ct = default)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));

        var depReport = await _dependencies.AnalyzeProjectAsync(project, ct);
        var callReport = await _calls.AnalyzeProjectAsync(project, ct);

        var relationships = new List<StaticRelationship>();
        relationships.AddRange(depReport.TypeEdges.Select(FromTypeEdge));
        relationships.AddRange(depReport.MethodEdges.Select(m => new StaticRelationship
        {
            Kind = StaticRelationshipKind.Uses,
            FromKey = m.FromMethodKey,
            FromDisplayName = m.FromMethodName,
            ToKey = m.ToTypeKey,
            ToDisplayName = m.ToTypeName,
            IsResolved = m.IsResolved,
            Detail = m.Kind.ToString(),
            ContainingDocument = m.ContainingDocument
        }));
        relationships.AddRange(callReport.AllEdges.Select(FromCallEdge));

        return Finalize(relationships);
    }

    private static StaticRelationshipReport Finalize(List<StaticRelationship> relationships)
    {
        var report = new StaticRelationshipReport
        {
            Relationships = relationships,
            Inheritance = ExtractInheritance(relationships),
            Composition = ExtractComposition(relationships),
            Invocations = ExtractInvocation(relationships),
            Uses = relationships.Where(r => r.Kind == StaticRelationshipKind.Uses).ToList()
        };

        report.TotalRelationships = relationships.Count;
        report.TotalInheritance = report.Inheritance.Count;
        report.TotalComposition = report.Composition.Count;
        report.TotalInvocations = report.Invocations.Count;
        report.TotalUses = report.Uses.Count;
        report.TotalUnresolved = relationships.Count(r => !r.IsResolved);

        report.UniqueSymbolKeys = relationships
            .SelectMany(r => new[] { r.FromKey, r.ToKey })
            .Where(k => !string.IsNullOrEmpty(k) && !k.StartsWith("UNKNOWN|"))
            .Distinct()
            .OrderBy(k => k)
            .ToList();

        return report;
    }
}