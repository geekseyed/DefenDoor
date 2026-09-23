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
/// BF-13.5: Caller / Callee Analysis Service
/// Stage 1: Caller discovery (declared methods → caller identity)
/// Stage 2: Callee discovery (invocations inside each method body)
/// Stage 3: Call relationship (edge: CallerKey → CalleeKey, via BF-13.4)
/// Stage 4: Call graph evidence (fan-in / fan-out metrics)
/// Principle: unresolved callee = UNKNOWN edge, never a failure.
/// </summary>
public class CallerCalleeAnalysisService
{
    private readonly SymbolResolutionService _symbols = new();

    // Stage 1
    public SymbolIdentity? ResolveCaller(
        SemanticModel model, MethodDeclarationSyntax methodDecl, CancellationToken ct = default)
    {
        var symbol = model.GetDeclaredSymbol(methodDecl, ct);
        return symbol is IMethodSymbol ? _symbols.BuildIdentity(symbol) : null;
    }

    // Stage 2
    public IReadOnlyList<InvocationExpressionSyntax> CollectInvocations(MethodDeclarationSyntax methodDecl)
    {
        if (methodDecl is null) throw new ArgumentNullException(nameof(methodDecl));
        return methodDecl.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
    }

    // Stage 3
    public CallEdge BuildEdge(
        SymbolIdentity? caller,
        string callerDisplayName,
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        string? documentName,
        CancellationToken ct = default)
    {
        if (invocation is null) throw new ArgumentNullException(nameof(invocation));
        if (model is null) throw new ArgumentNullException(nameof(model));

        var resolved = _symbols.ResolveInvokedSymbol(model, invocation, ct);

        return new CallEdge
        {
            CallerKey = caller?.ToStableKey(),
            CallerDisplayName = caller?.DisplayName ?? callerDisplayName,
            CalleeKey = resolved.Identity?.ToStableKey(),
            CalleeDisplayName = resolved.Identity?.DisplayName ?? invocation.ToString(),
            IsResolved = resolved.IsResolved,
            ContainingDocument = documentName
        };
    }

    // Stages 1-3 — document pass
    public async Task<DocumentCallEvidence> AnalyzeDocumentAsync(Document document, CancellationToken ct = default)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));

        var evidence = new DocumentCallEvidence { DocumentName = document.Name };

        var tree = await document.GetSyntaxTreeAsync(ct);
        if (tree is null) return evidence;
        var model = await document.GetSemanticModelAsync(ct);
        if (model is null) return evidence;

        var root = tree.GetRoot(ct);

        foreach (var methodDecl in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            evidence.DeclaredMethodCount++;

            var caller = ResolveCaller(model, methodDecl, ct);
            var callerName = methodDecl.Identifier.Text;

            foreach (var invocation in CollectInvocations(methodDecl))
            {
                var edge = BuildEdge(caller, callerName, invocation, model, document.Name, ct);
                evidence.Edges.Add(edge);
            }
        }

        evidence.ResolvedEdgeCount = evidence.Edges.Count(e => e.IsResolved);
        evidence.UnresolvedEdgeCount = evidence.Edges.Count(e => !e.IsResolved);
        return evidence;
    }

    // Stage 4 — project pass + metrics
    public async Task<CallerCalleeReport> AnalyzeProjectAsync(Project project, CancellationToken ct = default)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));

        var report = new CallerCalleeReport();
        foreach (var document in project.Documents)
        {
            ct.ThrowIfCancellationRequested();
            report.Documents.Add(await AnalyzeDocumentAsync(document, ct));
        }
        return Finalize(report);
    }

    // Stage 4 — fan-in / fan-out from resolved edges
    public static List<SymbolCallMetrics> ComputeMetrics(IEnumerable<CallEdge> edges)
    {
        if (edges is null) throw new ArgumentNullException(nameof(edges));

        var metrics = new Dictionary<string, SymbolCallMetrics>();

        void Ensure(string key, string display)
        {
            if (!metrics.ContainsKey(key))
                metrics[key] = new SymbolCallMetrics { SymbolKey = key, DisplayName = display };
        }

        foreach (var edge in edges.Where(e => e.IsResolved
                                              && e.CallerKey is not null
                                              && e.CalleeKey is not null))
        {
            Ensure(edge.CallerKey!, edge.CallerDisplayName);
            Ensure(edge.CalleeKey!, edge.CalleeDisplayName);

            metrics[edge.CallerKey!].Callees.Add(edge.CalleeKey!);
            metrics[edge.CalleeKey!].Callers.Add(edge.CallerKey!);
        }

        foreach (var m in metrics.Values)
        {
            m.FanIn = m.Callers.Distinct().Count();
            m.FanOut = m.Callees.Distinct().Count();
        }

        return metrics.Values.OrderBy(m => m.SymbolKey).ToList();
    }

    private static CallerCalleeReport Finalize(CallerCalleeReport report)
    {
        report.AllEdges = report.Documents.SelectMany(d => d.Edges).ToList();
        report.Metrics = ComputeMetrics(report.AllEdges);

        report.TotalDeclaredMethods = report.Documents.Sum(d => d.DeclaredMethodCount);
        report.TotalEdges = report.AllEdges.Count;
        report.TotalResolvedEdges = report.AllEdges.Count(e => e.IsResolved);
        report.TotalUnresolvedEdges = report.AllEdges.Count(e => !e.IsResolved);
        return report;
    }
}