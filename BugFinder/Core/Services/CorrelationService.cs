using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-05.1: Execution Path & Cross-Test Correlation
/// Analyzes multiple failures to find common execution paths and root causes.
/// </summary>
public class CorrelationService
{
    public CorrelationResult CorrelateFailures(List<Failure> failures)
    {
        var result = new CorrelationResult
        {
            TotalFailuresAnalyzed = failures.Count,
            Clusters = new List<FailureCluster>()
        };

        if (failures.Count == 0) return result;

        // Group by Shared Method (Strongest correlation)
        var methodGroups = failures
            .Where(f => f.Localization?.MethodName != null)
            .GroupBy(f => f.Localization!.MethodName!)
            .Where(g => g.Count() > 1)
            .Select(g => new FailureCluster
            {
                CorrelationStrength = CorrelationStrength.Strong,
                SharedElement = new SharedCodeElement
                {
                    ElementType = "Method",
                    Name = g.Key
                },
                RelatedFailureIds = g.Select(f => f.Identity.ToFullString()).ToList(),
                CommonStackTraceDepth = CalculateCommonDepth(g.ToList())
            })
            .ToList();

        // Group by Shared File (Medium correlation)
        var fileGroups = failures
            .Where(f => f.Localization?.PrimaryFilePath != null && !methodGroups.Any(mg => mg.RelatedFailureIds.Contains(f.Identity.ToFullString())))
            .GroupBy(f => System.IO.Path.GetFileName(f.Localization!.PrimaryFilePath!))
            .Where(g => g.Count() > 1)
            .Select(g => new FailureCluster
            {
                CorrelationStrength = CorrelationStrength.Medium,
                SharedElement = new SharedCodeElement
                {
                    ElementType = "File",
                    Name = g.Key
                },
                RelatedFailureIds = g.Select(f => f.Identity.ToFullString()).ToList(),
                CommonStackTraceDepth = CalculateCommonDepth(g.ToList())
            })
            .ToList();

        result.Clusters.AddRange(methodGroups);
        result.Clusters.AddRange(fileGroups);

        // Identify isolated failures
        var clusteredIds = result.Clusters.SelectMany(c => c.RelatedFailureIds).ToHashSet();
        result.IsolatedFailures = failures
            .Where(f => !clusteredIds.Contains(f.Identity.ToFullString()))
            .Select(f => f.Identity.ToFullString())
            .ToList();

        return result;
    }

    private int CalculateCommonDepth(List<Failure> clusterFailures)
    {
        // Simple heuristic: minimum stack depth in the cluster
        return clusterFailures.Min(f => f.Localization?.CandidateLocations?.Count ?? 0);
    }
}