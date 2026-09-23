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
/// BF-13.4: Symbol Resolution Service
/// Stage 1: Resolve type symbols (GetDeclaredSymbol)
/// Stage 2: Resolve method symbols (+ parameter signature)
/// Stage 3: Resolve property symbols
/// Stage 4: Build deterministic Symbol Identity (StableKey)
/// Principle: unresolved symbol = UNKNOWN record, never a failure.
/// </summary>
public class SymbolResolutionService
{
    // Stage 1 — type declarations (class / struct / interface / enum / record)
    public ResolvedSymbolEvidence ResolveTypeSymbol(
        SemanticModel model, BaseTypeDeclarationSyntax declaration, CancellationToken ct = default)
    {
        var symbol = model.GetDeclaredSymbol(declaration, ct);
        return symbol is ITypeSymbol typeSymbol
            ? BuildEvidence(declaration.Identifier.Text, typeSymbol)
            : Unresolved(declaration.Identifier.Text);
    }

    // Stage 2 — method declarations
    public ResolvedSymbolEvidence ResolveMethodSymbol(
        SemanticModel model, MethodDeclarationSyntax declaration, CancellationToken ct = default)
    {
        var symbol = model.GetDeclaredSymbol(declaration, ct);
        return symbol is IMethodSymbol
            ? BuildEvidence(declaration.Identifier.Text, symbol)
            : Unresolved(declaration.Identifier.Text);
    }

    // Stage 3 — property declarations
    public ResolvedSymbolEvidence ResolvePropertySymbol(
        SemanticModel model, PropertyDeclarationSyntax declaration, CancellationToken ct = default)
    {
        var symbol = model.GetDeclaredSymbol(declaration, ct);
        return symbol is IPropertySymbol
            ? BuildEvidence(declaration.Identifier.Text, symbol)
            : Unresolved(declaration.Identifier.Text);
    }

    // Call-site resolution — public hook for BF-13.5 Caller/Callee
    public ResolvedSymbolEvidence ResolveInvokedSymbol(
        SemanticModel model, InvocationExpressionSyntax invocation, CancellationToken ct = default)
    {
        var info = model.GetSymbolInfo(invocation.Expression, ct);
        return info.Symbol is ISymbol symbol
            ? BuildEvidence(invocation.ToString(), symbol)
            : Unresolved(invocation.ToString());
    }

    // Stage 4 — deterministic identity builder
    public SymbolIdentity BuildIdentity(ISymbol symbol)
    {
        var parameters = symbol switch
        {
            IMethodSymbol m =>
                "(" + string.Join(",", m.Parameters.Select(p => p.Type.ToDisplayString())) + ")",
            IPropertySymbol p when p.Parameters.Length > 0 =>
                "[" + string.Join(",", p.Parameters.Select(x => x.Type.ToDisplayString())) + "]",
            _ => null
        };

        return new SymbolIdentity
        {
            Kind = MapKind(symbol),
            AssemblyName = symbol.ContainingAssembly?.Name ?? "unknown",
            FullyQualifiedName = BuildFullyQualifiedName(symbol),
            Parameters = parameters,
            DisplayName = symbol.ToDisplayString()
        };
    }

    // Document pass — declarations + call sites
    public async Task<DocumentSymbolEvidence> AnalyzeDocumentAsync(Document document, CancellationToken ct = default)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));

        var evidence = new DocumentSymbolEvidence { DocumentName = document.Name };

        var tree = await document.GetSyntaxTreeAsync(ct);
        if (tree is null) return evidence;
        var model = await document.GetSemanticModelAsync(ct);
        if (model is null) return evidence;

        var root = tree.GetRoot(ct);

        foreach (var typeDecl in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            Add(evidence.Types, ResolveTypeSymbol(model, typeDecl, ct), evidence);

        foreach (var methodDecl in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            Add(evidence.Methods, ResolveMethodSymbol(model, methodDecl, ct), evidence);

        foreach (var propDecl in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
            Add(evidence.Properties, ResolvePropertySymbol(model, propDecl, ct), evidence);

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            Add(evidence.Invocations, ResolveInvokedSymbol(model, invocation, ct), evidence);

        return evidence;
    }

    // Project pass — aggregation + unique symbol registry
    public async Task<SymbolResolutionReport> AnalyzeProjectAsync(Project project, CancellationToken ct = default)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));

        var report = new SymbolResolutionReport();
        foreach (var document in project.Documents)
        {
            ct.ThrowIfCancellationRequested();
            report.Documents.Add(await AnalyzeDocumentAsync(document, ct));
        }
        return Finalize(report);
    }

    // ---------- internals ----------

    private static void Add(
        List<ResolvedSymbolEvidence> bucket, ResolvedSymbolEvidence item, DocumentSymbolEvidence owner)
    {
        bucket.Add(item);
        if (!item.IsResolved) owner.UnresolvedCount++;
    }

    private static SymbolResolutionReport Finalize(SymbolResolutionReport report)
    {
        report.TotalDocuments = report.Documents.Count;
        report.TotalTypes = report.Documents.Sum(d => d.Types.Count);
        report.TotalDeclaredMethods = report.Documents.Sum(d => d.Methods.Count);
        report.TotalDeclaredProperties = report.Documents.Sum(d => d.Properties.Count);
        report.TotalInvocations = report.Documents.Sum(d => d.Invocations.Count);

        var all = report.Documents
            .SelectMany(d => d.Types.Concat(d.Methods).Concat(d.Properties).Concat(d.Invocations))
            .ToList();

        report.TotalResolved = all.Count(e => e.IsResolved);
        report.TotalUnresolved = all.Count(e => !e.IsResolved);

        report.UniqueSymbolKeys = all
            .Where(e => e.IsResolved && e.Identity is not null)
            .Select(e => e.Identity!.ToStableKey())
            .Distinct()
            .OrderBy(k => k)
            .ToList();

        return report;
    }

    private ResolvedSymbolEvidence BuildEvidence(string syntaxIdentifier, ISymbol symbol)
    {
        return new ResolvedSymbolEvidence
        {
            SyntaxIdentifier = syntaxIdentifier,
            Kind = MapKind(symbol),
            IsResolved = true,
            Identity = BuildIdentity(symbol),
            Accessibility = symbol.DeclaredAccessibility.ToString(),
            IsStatic = symbol is IMethodSymbol m && m.IsStatic
                    || symbol is IPropertySymbol p && p.IsStatic,
            ContainingTypeName = symbol.ContainingType?.Name,
            ContainingNamespace = symbol.ContainingNamespace is { IsGlobalNamespace: false } ns
                ? ns.ToDisplayString()
                : null
        };
    }

    private static ResolvedSymbolEvidence Unresolved(string syntaxIdentifier) => new()
    {
        SyntaxIdentifier = syntaxIdentifier,
        Kind = SemanticSymbolKind.Unknown,
        IsResolved = false
    };

    private static string BuildFullyQualifiedName(ISymbol symbol)
    {
        if (symbol is ITypeSymbol typeSymbol)
            return typeSymbol.ToDisplayString();                       // e.g. Lib.Calculator

        if (symbol.ContainingType is { } containingType)
            return $"{containingType.ToDisplayString()}.{symbol.Name}"; // e.g. Lib.Calculator.Add

        if (symbol.ContainingNamespace is { IsGlobalNamespace: false } ns)
            return $"{ns.ToDisplayString()}.{symbol.Name}";

        return symbol.Name;
    }

    private static SemanticSymbolKind MapKind(ISymbol symbol) => symbol switch
    {
        IMethodSymbol => SemanticSymbolKind.Method,
        IPropertySymbol => SemanticSymbolKind.Property,
        IFieldSymbol => SemanticSymbolKind.Field,
        ITypeSymbol => SemanticSymbolKind.Type,
        _ => SemanticSymbolKind.Unknown
    };
}