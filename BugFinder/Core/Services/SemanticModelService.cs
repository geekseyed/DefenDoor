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
/// BF-13.3: Semantic Model Service
/// Stage 1: Load semantic model per document
/// Stage 2: Type information (GetTypeInfo) on invocation results
/// Stage 3: Symbol information (GetSymbolInfo + GetDeclaredSymbol)
/// Stage 4: Typed semantic evidence per document + aggregated report
/// Principle applied: unresolved symbol = UNKNOWN, not a failure.
/// </summary>
public class SemanticModelService
{
    // Stage 1
    public async Task<SemanticModel?> GetSemanticModelAsync(Document document, CancellationToken ct = default)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        return await document.GetSemanticModelAsync(ct);
    }

    // Stages 2-4 — full document analysis
    public async Task<DocumentSemanticEvidence> AnalyzeDocumentAsync(Document document, CancellationToken ct = default)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));

        var evidence = new DocumentSemanticEvidence { DocumentName = document.Name };

        var tree = await document.GetSyntaxTreeAsync(ct);
        if (tree is null) return evidence;

        var model = await document.GetSemanticModelAsync(ct);
        if (model is null) return evidence;

        var root = tree.GetRoot(ct);

        // Stage 3 — declared method symbols
        foreach (var methodDecl in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var symbol = model.GetDeclaredSymbol(methodDecl, ct);
            if (symbol is IMethodSymbol method)
            {
                evidence.DeclaredMethods.Add(new MethodSemanticEvidence
                {
                    MethodName = method.Name,
                    FullSignature = method.ToDisplayString(),
                    ReturnType = method.ReturnType.ToDisplayString(),
                    ParameterCount = method.Parameters.Length,
                    ParameterTypes = method.Parameters
                        .Select(p => p.Type.ToDisplayString()).ToList(),
                    IsStatic = method.IsStatic
                });
            }
            else
            {
                // Symbol unavailable → syntax-level fallback, NOT an error
                evidence.DeclaredMethods.Add(new MethodSemanticEvidence
                {
                    MethodName = methodDecl.Identifier.Text,
                    ParameterCount = methodDecl.ParameterList.Parameters.Count
                });
            }
        }

        // Stage 2 + Stage 3 — invocations
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var identifier = invocation.Expression.ToString();

            // Stage 2 — result type of the invocation expression
            var typeInfo = model.GetTypeInfo(invocation, ct);
            if (typeInfo.Type is ITypeSymbol typeSymbol)
            {
                evidence.ExpressionTypes.Add(new SemanticTypeInfo
                {
                    Expression = identifier,
                    TypeName = typeSymbol.Name,
                    FullTypeName = typeSymbol.ToDisplayString(),
                    Namespace = typeSymbol.ContainingNamespace?.ToDisplayString(),
                    Kind = typeSymbol.TypeKind.ToString(),
                    IsResolved = true
                });
            }

            // Stage 3 — symbol behind the invocation
            var symbolInfo = model.GetSymbolInfo(invocation.Expression, ct);
            if (symbolInfo.Symbol is ISymbol resolved)
            {
                evidence.InvokedSymbols.Add(new SemanticSymbolEvidence
                {
                    SyntaxIdentifier = identifier,
                    Kind = MapKind(resolved),
                    IsResolved = true,
                    DisplayName = resolved.ToDisplayString(),
                    ContainingTypeName = resolved.ContainingType?.Name,
                    ContainingNamespace = resolved.ContainingNamespace?.ToDisplayString(),
                    Accessibility = resolved.DeclaredAccessibility.ToString(),
                    IsStatic = resolved is IMethodSymbol m && m.IsStatic
                });
            }
            else
            {
                evidence.InvokedSymbols.Add(new SemanticSymbolEvidence
                {
                    SyntaxIdentifier = identifier,
                    Kind = SemanticSymbolKind.Unknown,
                    IsResolved = false
                });
                evidence.UnresolvedCount++;
            }
        }

        return evidence;
    }

    // Stage 4 — project-wide aggregation
    public async Task<SemanticAnalysisReport> AnalyzeProjectAsync(Project project, CancellationToken ct = default)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));

        var report = new SemanticAnalysisReport();
        foreach (var document in project.Documents)
        {
            ct.ThrowIfCancellationRequested();
            report.Documents.Add(await AnalyzeDocumentAsync(document, ct));
        }
        return Finalize(report);
    }

    private static SemanticAnalysisReport Finalize(SemanticAnalysisReport report)
    {
        report.TotalDocuments = report.Documents.Count;
        report.TotalMethods = report.Documents.Sum(d => d.DeclaredMethods.Count);
        report.TotalResolvedSymbols = report.Documents.Sum(d => d.InvokedSymbols.Count(s => s.IsResolved));
        report.TotalUnresolvedSymbols = report.Documents.Sum(d => d.UnresolvedCount);
        return report;
    }

    private static SemanticSymbolKind MapKind(ISymbol symbol) => symbol switch
    {
        IMethodSymbol => SemanticSymbolKind.Method,
        IPropertySymbol => SemanticSymbolKind.Property,
        IFieldSymbol => SemanticSymbolKind.Field,
        IParameterSymbol => SemanticSymbolKind.Parameter,
        ILocalSymbol => SemanticSymbolKind.Local,
        ITypeSymbol => SemanticSymbolKind.Type,
        _ => SemanticSymbolKind.Unknown
    };
}