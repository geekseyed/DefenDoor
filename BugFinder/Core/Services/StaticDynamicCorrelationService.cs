using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ISCM.BugFinder.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-13.8: Static + Dynamic Correlation Service
/// Stage 1: Dynamic Location → Symbol (file+line+method → StableKey)
/// Stage 2: Dynamic Stack → Code Structure (frame method name → method)
/// Stage 3: Coverage → Symbol (executed line → containing method)
/// Stage 4: Runtime → Code Structure (same location mechanism)
/// Stage 5: Correlated Code Evidence (report + dynamic-supported symbols)
/// Principle: uncorrelated = UNKNOWN (strategy None), never a failure.
/// </summary>
public class StaticDynamicCorrelationService
{
    private readonly SymbolResolutionService _symbols = new();

    // Static index — one declared method per entry (resolvable only)
    public async Task<List<StaticMethodIndexEntry>> BuildMethodIndexAsync(
        Project project, CancellationToken ct = default)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));

        var index = new List<StaticMethodIndexEntry>();

        foreach (var document in project.Documents)
        {
            ct.ThrowIfCancellationRequested();

            var tree = await document.GetSyntaxTreeAsync(ct);
            if (tree is null) continue;
            var model = await document.GetSemanticModelAsync(ct);
            if (model is null) continue;

            // in-memory docs have no FilePath — document name is the file reference
            var fileRef = !string.IsNullOrEmpty(document.FilePath) ? document.FilePath : document.Name;

            var root = tree.GetRoot(ct);
            foreach (var methodDecl in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var symbol = model.GetDeclaredSymbol(methodDecl, ct) as IMethodSymbol;
                if (symbol is null) continue; // unresolved declaration → no static anchor

                var identity = _symbols.BuildIdentity(symbol);
                var span = methodDecl.GetLocation().GetLineSpan();

                index.Add(new StaticMethodIndexEntry
                {
                    SymbolKey = identity.ToStableKey(),
                    DisplayName = identity.DisplayName,
                    MethodName = methodDecl.Identifier.Text,
                    ContainingTypeName = symbol.ContainingType?.Name ?? string.Empty,
                    ContainingNamespace = symbol.ContainingNamespace is { IsGlobalNamespace: false } ns
                        ? ns.ToDisplayString() : null,
                    FilePath = fileRef,
                    StartLine = span.StartLinePosition.Line + 1,
                    EndLine = span.EndLinePosition.Line + 1,
                    IsStatic = symbol.IsStatic,
                    ParameterCount = symbol.Parameters.Length
                });
            }
        }

        return index;
    }

    // Stage 1-4 — correlate ONE dynamic item against the index
    public CorrelatedCodeEvidence Correlate(
        DynamicLocationEvidence dynamic, IReadOnlyList<StaticMethodIndexEntry> index)
    {
        if (dynamic is null) throw new ArgumentNullException(nameof(dynamic));
        if (index is null) throw new ArgumentNullException(nameof(index));

        var result = new CorrelatedCodeEvidence { Dynamic = dynamic };

        // file is a hard constraint when provided
        var candidates = index.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(dynamic.FilePath))
            candidates = candidates.Where(e => SameFile(e.FilePath, dynamic.FilePath!));

        var pool = candidates.ToList();

        // Strategy 1: MethodName + File
        if (!string.IsNullOrWhiteSpace(dynamic.MethodName) && pool.Count > 0)
        {
            var simple = ExtractSimpleMethodName(dynamic.MethodName!);
            var byName = pool.Where(e => e.MethodName == simple).ToList();
            if (byName.Count > 0)
                return FinalizeMatch(result, byName, CorrelationMatchStrategy.MethodNameAndFile, 1.0);
        }

        // Strategy 2: Line within method span
        if (dynamic.LineNumber is int line && pool.Count > 0)
        {
            var byLine = pool.Where(e => e.StartLine <= line && line <= e.EndLine).ToList();
            if (byLine.Count > 0)
                return FinalizeMatch(result, byLine, CorrelationMatchStrategy.LineWithinMethod, 0.9);
        }

        // Strategy 3: MethodName only (no file or file had no entries)
        if (!string.IsNullOrWhiteSpace(dynamic.MethodName))
        {
            var simple = ExtractSimpleMethodName(dynamic.MethodName!);
            var byName = index.Where(e => e.MethodName == simple).ToList();
            if (byName.Count > 0)
                return FinalizeMatch(result, byName, CorrelationMatchStrategy.MethodNameOnly, 0.7);
        }

        // Strategy 4: File only
        if (pool.Count > 0)
            return FinalizeMatch(result, pool, CorrelationMatchStrategy.FileOnly, 0.4);

        // No match → UNKNOWN, never failure
        result.Strategy = CorrelationMatchStrategy.None;
        result.IsCorrelated = false;
        result.MatchConfidence = 0.0;
        return result;
    }

    // Stage 5 — correlate a batch + aggregate
    public StaticDynamicCorrelationReport CorrelateAll(
        IEnumerable<DynamicLocationEvidence>? dynamicItems,
        IReadOnlyList<StaticMethodIndexEntry> index)
    {
        var items = (dynamicItems ?? Enumerable.Empty<DynamicLocationEvidence>())
            .Select(d => Correlate(d, index))
            .ToList();
        return Finalize(items, index.Count);
    }

    // BF-12 → BF-13 handshake: ranked candidates are dynamic locations
    public async Task<StaticDynamicCorrelationReport> CorrelateRankedResultAsync(
        RankedLocalizationResult rankedResult, Project project, CancellationToken ct = default)
    {
        if (rankedResult is null) throw new ArgumentNullException(nameof(rankedResult));
        if (project is null) throw new ArgumentNullException(nameof(project));

        var index = await BuildMethodIndexAsync(project, ct);

        var dynamicItems = rankedResult.Candidates.Select(c => new DynamicLocationEvidence
        {
            SourceType = DynamicEvidenceSource.RankedCandidate,
            SourceArtifact = c.ElementId,
            FilePath = c.FilePath,
            LineNumber = c.LineNumber,
            SignalStrength = c.UnifiedScore,
            Description = $"Rank {c.Rank} ({c.ConfidenceLevel})"
        });

        return CorrelateAll(dynamicItems, index);
    }

    // ---------- internals ----------

    private static CorrelatedCodeEvidence FinalizeMatch(
        CorrelatedCodeEvidence result,
        List<StaticMethodIndexEntry> matches,
        CorrelationMatchStrategy strategy,
        double confidence)
    {
        result.MatchedMethods = matches;
        result.Strategy = strategy;
        result.IsCorrelated = true;
        result.MatchConfidence = confidence;
        result.PrimarySymbolKey = matches[0].SymbolKey;
        result.PrimaryDisplayName = matches[0].DisplayName;
        return result;
    }

    private static StaticDynamicCorrelationReport Finalize(
        List<CorrelatedCodeEvidence> items, int indexSize)
    {
        var report = new StaticDynamicCorrelationReport
        {
            Items = items,
            StaticIndexSize = indexSize
        };

        report.TotalDynamicEvidence = items.Count;
        report.TotalCorrelated = items.Count(i => i.IsCorrelated);
        report.TotalUncorrelated = items.Count(i => !i.IsCorrelated);
        report.CorrelationRate = report.TotalDynamicEvidence == 0
            ? 0.0
            : (double)report.TotalCorrelated / report.TotalDynamicEvidence;

        report.SymbolKeysWithDynamicSupport = items
            .Where(i => i.IsCorrelated && i.PrimarySymbolKey is not null)
            .Select(i => i.PrimarySymbolKey!)
            .Distinct()
            .OrderBy(k => k)
            .ToList();

        return report;
    }

    // "Namespace.Class.Method(int)" / "Class.Method" / "Method" → "Method"
    private static string ExtractSimpleMethodName(string raw) =>
        raw.Split('(')[0].Trim().Split('.').Last();

    private static bool SameFile(string a, string b) =>
        string.Equals(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase);
}