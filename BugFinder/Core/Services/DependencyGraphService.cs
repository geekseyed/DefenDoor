using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ISCM.BugFinder.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-13.6: Dependency Graph Service
/// Stage 1: Project dependencies (Project.ProjectReferences)
/// Stage 2: Type dependencies (inheritance, fields, properties, params,
///          returns, instantiations, static member access)
/// Stage 3: Method dependencies (method → type usage)
/// Stage 4: Graph assembly (nodes + FanIn/FanOut per node)
/// Principles:
///   - Creation types resolve via GetTypeInfo(creation) (whole expression),
///     NOT GetTypeInfo(creation.Type) — type syntax inside new T() is not
///     bound as an expression.
///   - ErrorTypeSymbol (missing types) = UNKNOWN, never resolved.
///   - Unresolved type reference = UNKNOWN edge, never failure.
/// </summary>
public class DependencyGraphService
{
    private readonly SymbolResolutionService _symbols = new();

    // Stage 1 — dependencies of ONE project
    public List<ProjectDependencyEdge> CollectProjectDependencies(Project project)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));

        var edges = new List<ProjectDependencyEdge>();
        foreach (var projectRef in project.ProjectReferences)
        {
            var dependency = project.Solution.GetProject(projectRef.ProjectId);
            if (dependency is null) continue;
            edges.Add(new ProjectDependencyEdge
            {
                FromProject = project.Name,
                ToProject = dependency.Name
            });
        }
        return edges;
    }

    // Stage 2 — type-level dependencies of one declared type
    public List<TypeDependencyEdge> AnalyzeTypeDependencies(
        SemanticModel model,
        TypeDeclarationSyntax typeDecl,
        string? documentName,
        CancellationToken ct = default)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));
        if (typeDecl is null) throw new ArgumentNullException(nameof(typeDecl));

        var edges = new List<TypeDependencyEdge>();

        var fromSymbol = model.GetDeclaredSymbol(typeDecl, ct) as ITypeSymbol;
        var fromKey = fromSymbol is not null
            ? _symbols.BuildIdentity(fromSymbol).ToStableKey()
            : $"UNKNOWN|{typeDecl.Identifier.Text}";
        var fromName = fromSymbol?.ToDisplayString() ?? typeDecl.Identifier.Text;

        void AddDeclaredTypeEdge(TypeSyntax? syntax, TypeDependencyKind kind)
        {
            if (syntax is null) return;
            var type = ResolveDeclaredType(model, syntax, ct);
            edges.Add(type is null
                ? UnresolvedTypeEdge(fromKey, fromName, syntax.ToString(), kind, documentName)
                : ResolvedTypeEdge(fromKey, fromName, type, kind, documentName));
        }

        // inheritance / interfaces
        if (typeDecl.BaseList is not null)
            foreach (var baseType in typeDecl.BaseList.Types)
                AddDeclaredTypeEdge(baseType.Type, TypeDependencyKind.Inheritance);

        // direct members: fields / properties / method signatures
        foreach (var field in typeDecl.Members.OfType<FieldDeclarationSyntax>())
            AddDeclaredTypeEdge(field.Declaration.Type, TypeDependencyKind.FieldType);

        foreach (var property in typeDecl.Members.OfType<PropertyDeclarationSyntax>())
            AddDeclaredTypeEdge(property.Type, TypeDependencyKind.PropertyType);

        foreach (var method in typeDecl.Members.OfType<MethodDeclarationSyntax>())
        {
            AddDeclaredTypeEdge(method.ReturnType, TypeDependencyKind.ReturnType);
            foreach (var parameter in method.ParameterList.Parameters)
                AddDeclaredTypeEdge(parameter.Type, TypeDependencyKind.ParameterType);
        }

        // instantiations — resolve via the WHOLE creation expression
        foreach (var creation in typeDecl.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
                     .Where(n => !IsInsideNestedType(n, typeDecl)))
        {
            var createdType = ResolveCreatedType(model, creation, ct);
            edges.Add(createdType is null
                ? UnresolvedTypeEdge(fromKey, fromName, creation.Type.ToString(),
                    TypeDependencyKind.Instantiation, documentName)
                : ResolvedTypeEdge(fromKey, fromName, createdType,
                    TypeDependencyKind.Instantiation, documentName));
        }

        // static member access (excluding nested types)
        foreach (var memberAccess in typeDecl.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                     .Where(n => !IsInsideNestedType(n, typeDecl)))
        {
            if (model.GetSymbolInfo(memberAccess.Expression, ct).Symbol is not ITypeSymbol staticType
                || staticType.TypeKind == TypeKind.Error)
                continue;
            var identity = _symbols.BuildIdentity(staticType);
            edges.Add(new TypeDependencyEdge
            {
                FromTypeKey = fromKey,
                FromTypeName = fromName,
                ToTypeKey = identity.ToStableKey(),
                ToTypeName = identity.DisplayName,
                Kind = TypeDependencyKind.StaticAccess,
                IsResolved = true,
                ContainingDocument = documentName
            });
        }

        return edges;
    }

    // Stage 3 — method → type usage edges
    public List<MethodTypeDependencyEdge> AnalyzeMethodDependencies(
        SemanticModel model,
        MethodDeclarationSyntax methodDecl,
        string? documentName,
        CancellationToken ct = default)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));
        if (methodDecl is null) throw new ArgumentNullException(nameof(methodDecl));

        var edges = new List<MethodTypeDependencyEdge>();

        var methodSymbol = model.GetDeclaredSymbol(methodDecl, ct) as IMethodSymbol;
        var fromKey = methodSymbol is not null
            ? _symbols.BuildIdentity(methodSymbol).ToStableKey()
            : $"UNKNOWN|{methodDecl.Identifier.Text}";
        var fromName = methodSymbol?.ToDisplayString() ?? methodDecl.Identifier.Text;

        foreach (var creation in methodDecl.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            var createdType = ResolveCreatedType(model, creation, ct);
            edges.Add(createdType is null
                ? UnresolvedMethodEdge(fromKey, fromName, creation.Type.ToString(),
                    TypeDependencyKind.Instantiation, documentName)
                : ResolvedMethodEdge(fromKey, fromName, createdType,
                    TypeDependencyKind.Instantiation, documentName));
        }

        foreach (var memberAccess in methodDecl.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (model.GetSymbolInfo(memberAccess.Expression, ct).Symbol is not ITypeSymbol staticType
                || staticType.TypeKind == TypeKind.Error)
                continue;
            var identity = _symbols.BuildIdentity(staticType);
            edges.Add(new MethodTypeDependencyEdge
            {
                FromMethodKey = fromKey,
                FromMethodName = fromName,
                ToTypeKey = identity.ToStableKey(),
                ToTypeName = identity.DisplayName,
                Kind = TypeDependencyKind.StaticAccess,
                IsResolved = true,
                ContainingDocument = documentName
            });
        }

        return edges;
    }

    // Document pass
    public async Task<DocumentDependencyEvidence> AnalyzeDocumentAsync(Document document, CancellationToken ct = default)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));

        var evidence = new DocumentDependencyEvidence { DocumentName = document.Name };

        var tree = await document.GetSyntaxTreeAsync(ct);
        if (tree is null) return evidence;
        var model = await document.GetSemanticModelAsync(ct);
        if (model is null) return evidence;

        var root = tree.GetRoot(ct);

        foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            evidence.TypeEdges.AddRange(AnalyzeTypeDependencies(model, typeDecl, document.Name, ct));

        foreach (var methodDecl in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            evidence.MethodEdges.AddRange(AnalyzeMethodDependencies(model, methodDecl, document.Name, ct));

        return evidence;
    }

    // Project pass + Stage 4 graph assembly
    public async Task<DependencyGraphReport> AnalyzeProjectAsync(Project project, CancellationToken ct = default)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));

        var report = new DependencyGraphReport
        {
            ProjectEdges = CollectProjectDependencies(project)
        };

        foreach (var document in project.Documents)
        {
            ct.ThrowIfCancellationRequested();
            report.Documents.Add(await AnalyzeDocumentAsync(document, ct));
        }

        return Finalize(report);
    }

    // ---------- internals ----------

    // Declared type positions (fields/props/params/returns) bind via GetTypeInfo(syntax)
    private static ITypeSymbol? ResolveDeclaredType(SemanticModel model, TypeSyntax syntax, CancellationToken ct)
    {
        var type = model.GetTypeInfo(syntax, ct).Type;
        if (type is null || type.TypeKind == TypeKind.Error) return null;
        return type;
    }

    // Creation types bind via GetTypeInfo(creation) — the WHOLE expression
    private static ITypeSymbol? ResolveCreatedType(SemanticModel model, ObjectCreationExpressionSyntax creation, CancellationToken ct)
    {
        var type = model.GetTypeInfo(creation, ct).Type;
        if (type is null || type.TypeKind == TypeKind.Error) return null;
        return type;
    }

    private TypeDependencyEdge ResolvedTypeEdge(
        string fromKey, string fromName, ITypeSymbol type, TypeDependencyKind kind, string? documentName)
    {
        var identity = _symbols.BuildIdentity(type);
        return new TypeDependencyEdge
        {
            FromTypeKey = fromKey,
            FromTypeName = fromName,
            ToTypeKey = identity.ToStableKey(),
            ToTypeName = identity.DisplayName,
            Kind = kind,
            IsResolved = true,
            ContainingDocument = documentName
        };
    }

    private static TypeDependencyEdge UnresolvedTypeEdge(
        string fromKey, string fromName, string rawTypeName, TypeDependencyKind kind, string? documentName)
    {
        return new TypeDependencyEdge
        {
            FromTypeKey = fromKey,
            FromTypeName = fromName,
            ToTypeKey = null,
            ToTypeName = rawTypeName,
            Kind = kind,
            IsResolved = false,
            ContainingDocument = documentName
        };
    }

    private MethodTypeDependencyEdge ResolvedMethodEdge(
        string fromKey, string fromName, ITypeSymbol type, TypeDependencyKind kind, string? documentName)
    {
        var identity = _symbols.BuildIdentity(type);
        return new MethodTypeDependencyEdge
        {
            FromMethodKey = fromKey,
            FromMethodName = fromName,
            ToTypeKey = identity.ToStableKey(),
            ToTypeName = identity.DisplayName,
            Kind = kind,
            IsResolved = true,
            ContainingDocument = documentName
        };
    }

    private static MethodTypeDependencyEdge UnresolvedMethodEdge(
        string fromKey, string fromName, string rawTypeName, TypeDependencyKind kind, string? documentName)
    {
        return new MethodTypeDependencyEdge
        {
            FromMethodKey = fromKey,
            FromMethodName = fromName,
            ToTypeKey = null,
            ToTypeName = rawTypeName,
            Kind = kind,
            IsResolved = false,
            ContainingDocument = documentName
        };
    }

    private static bool IsInsideNestedType(SyntaxNode node, TypeDeclarationSyntax owner)
    {
        for (var current = node.Parent; current is not null && current != owner; current = current.Parent)
            if (current is TypeDeclarationSyntax)
                return true;
        return false;
    }

    private static DependencyGraphReport Finalize(DependencyGraphReport report)
    {
        // Flatten document-level edges into graph level (Stage 4 input)
        report.TypeEdges = report.Documents.SelectMany(d => d.TypeEdges).ToList();
        report.MethodEdges = report.Documents.SelectMany(d => d.MethodEdges).ToList();

        // dedupe identical edges — key includes raw target name so two
        // unresolved edges from the same source never collapse into one
        report.TypeEdges = report.TypeEdges
            .GroupBy(e => $"{e.FromTypeKey}>{e.ToTypeKey}>{e.ToTypeName}>{e.Kind}")
            .Select(g => g.First()).ToList();
        report.MethodEdges = report.MethodEdges
            .GroupBy(e => $"{e.FromMethodKey}>{e.ToTypeKey}>{e.ToTypeName}>{e.Kind}")
            .Select(g => g.First()).ToList();

        report.TotalProjects = report.ProjectEdges
            .SelectMany(e => new[] { e.FromProject, e.ToProject }).Distinct().Count();
        report.TotalProjectDependencies = report.ProjectEdges.Count;
        report.TotalTypeEdges = report.TypeEdges.Count;
        report.TotalMethodEdges = report.MethodEdges.Count;
        report.TotalUnresolvedTypeEdges = report.TypeEdges.Count(e => !e.IsResolved);

        report.Nodes = BuildNodes(report);
        return report;
    }

    private static List<DependencyNode> BuildNodes(DependencyGraphReport report)
    {
        var nodes = new Dictionary<string, DependencyNode>();
        var fanIn = new Dictionary<string, HashSet<string>>();
        var fanOut = new Dictionary<string, HashSet<string>>();

        void Ensure(string key, string display, string level)
        {
            if (!nodes.ContainsKey(key))
                nodes[key] = new DependencyNode { Key = key, DisplayName = display, Level = level };
        }
        void Link(string from, string to)
        {
            if (!fanOut.TryGetValue(from, out var outSet)) fanOut[from] = outSet = new HashSet<string>();
            outSet.Add(to);
            if (!fanIn.TryGetValue(to, out var inSet)) fanIn[to] = inSet = new HashSet<string>();
            inSet.Add(from);
        }

        foreach (var e in report.ProjectEdges)
        {
            Ensure("P|" + e.FromProject, e.FromProject, "Project");
            Ensure("P|" + e.ToProject, e.ToProject, "Project");
            Link("P|" + e.FromProject, "P|" + e.ToProject);
        }
        foreach (var e in report.TypeEdges)
        {
            Ensure(e.FromTypeKey, e.FromTypeName, "Type");
            if (e.IsResolved && e.ToTypeKey is not null)
            {
                Ensure(e.ToTypeKey, e.ToTypeName!, "Type");
                Link(e.FromTypeKey, e.ToTypeKey);
            }
        }
        foreach (var e in report.MethodEdges)
        {
            Ensure(e.FromMethodKey, e.FromMethodName, "Method");
            if (e.IsResolved && e.ToTypeKey is not null)
                Link(e.FromMethodKey, e.ToTypeKey);
        }

        foreach (var node in nodes.Values)
        {
            node.FanIn = fanIn.TryGetValue(node.Key, out var i) ? i.Count : 0;
            node.FanOut = fanOut.TryGetValue(node.Key, out var o) ? o.Count : 0;
        }

        return nodes.Values.OrderBy(n => n.Level).ThenBy(n => n.Key).ToList();
    }
}