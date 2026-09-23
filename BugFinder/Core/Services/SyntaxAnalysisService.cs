using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ISCM.BugFinder.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-13.2: Syntax Analysis Service
/// Stage 1: Load Syntax Tree (parse source / collect project trees)
/// Stage 2: Identify Syntax Nodes (walk descendants)
/// Stage 3: Resolve Method Body (block or expression-bodied)
/// Stage 4: Build Syntax Evidence (typed evidence per tree + report)
/// Syntax-only by design — no semantic model (that is BF-13.3).
/// </summary>
public class SyntaxAnalysisService
{
    // Stage 1 — parse raw source text
    public SyntaxTree ParseSource(string source, string? filePath = null)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        return CSharpSyntaxTree.ParseText(source, path: filePath ?? string.Empty);
    }

    // Stages 2-3 — analyze one tree
    public SyntaxTreeEvidence AnalyzeTree(SyntaxTree tree)
    {
        if (tree is null) throw new ArgumentNullException(nameof(tree));

        var root = tree.GetRoot();

        var evidence = new SyntaxTreeEvidence
        {
            FilePath = string.IsNullOrEmpty(tree.FilePath) ? null : tree.FilePath,
            TotalNodeCount = root.DescendantNodesAndTokens().Count(),
            TypeCount = root.DescendantNodes().OfType<TypeDeclarationSyntax>().Count()
        };

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var span = method.GetLocation().GetLineSpan();
            evidence.Methods.Add(new MethodSyntaxEvidence
            {
                MethodName = method.Identifier.Text,
                ContainingTypeName = ResolveContainingTypeName(method),
                FilePath = evidence.FilePath,
                StartLine = span.StartLinePosition.Line + 1,
                EndLine = span.EndLinePosition.Line + 1,
                ParameterCount = method.ParameterList.Parameters.Count,
                StatementCount = method.Body?.Statements.Count ?? 0,
                HasExecutableBody = method.Body is not null || method.ExpressionBody is not null,
                IsExpressionBodied = method.ExpressionBody is not null
            });
        }

        evidence.MethodCount = evidence.Methods.Count;
        return evidence;
    }

    // Stage 4 — aggregate report over many trees
    public SyntaxAnalysisReport Analyze(IEnumerable<SyntaxTree>? trees)
    {
        var report = new SyntaxAnalysisReport();
        foreach (var tree in trees ?? Enumerable.Empty<SyntaxTree>())
        {
            report.Trees.Add(AnalyzeTree(tree));
        }
        return Finalize(report);
    }

    // Stage 4 — full project pass (all documents)
    public async Task<SyntaxAnalysisReport> AnalyzeProjectAsync(Project project, CancellationToken ct = default)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));

        var report = new SyntaxAnalysisReport();
        foreach (var doc in project.Documents)
        {
            ct.ThrowIfCancellationRequested();
            var tree = await doc.GetSyntaxTreeAsync(ct);
            if (tree is null) continue;
            report.Trees.Add(AnalyzeTree(tree));
        }
        return Finalize(report);
    }

    private static SyntaxAnalysisReport Finalize(SyntaxAnalysisReport report)
    {
        report.TotalTrees = report.Trees.Count;
        report.TotalMethods = report.Trees.Sum(t => t.MethodCount);
        report.TotalTypes = report.Trees.Sum(t => t.TypeCount);
        return report;
    }

    // Syntax-only containment: walk up to the nearest type declaration
    private static string ResolveContainingTypeName(MethodDeclarationSyntax method)
    {
        SyntaxNode? current = method.Parent;
        while (current is not null && current is not TypeDeclarationSyntax)
            current = current.Parent;
        return (current as TypeDeclarationSyntax)?.Identifier.Text ?? string.Empty;
    }
}