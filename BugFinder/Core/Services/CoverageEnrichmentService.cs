using ISCM.BugFinder.Core.Models;
using System.Linq;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-06.2: Coverage-Failure Correlation
/// Enriches failures with coverage data to distinguish tested vs untested code failures.
/// </summary>
public class CoverageEnrichmentService
{
    /// <summary>
    /// Analyzes failures against coverage data and adds metadata.
    /// </summary>
    public void EnrichFailures(List<Failure> failures, CoverageSession coverageSession)
    {
        if (failures == null || coverageSession == null) return;

        // Build a lookup map: ClassName -> LineNumber -> IsCovered
        var coverageMap = new Dictionary<string, Dictionary<int, bool>>();

        foreach (var module in coverageSession.Modules)
        {
            foreach (var cls in module.Classes)
            {
                var classNameKey = $"{cls.Namespace}.{cls.ClassName}";

                if (!coverageMap.ContainsKey(classNameKey))
                {
                    coverageMap[classNameKey] = new Dictionary<int, bool>();
                    foreach (var line in cls.Lines)
                    {
                        coverageMap[classNameKey][line.LineNumber] = line.IsCovered;
                    }
                }
            }
        }

        foreach (var failure in failures)
        {
            if (failure.Localization == null) continue;

            bool? isCovered = null;
            var targetClassName = failure.Identity.ClassName;

            if (!string.IsNullOrEmpty(targetClassName))
            {
                // Try exact match first
                if (coverageMap.TryGetValue(targetClassName, out var exactLineMap))
                {
                    var targetLine = failure.Localization.PrimaryLineNumber;
                    if (targetLine.HasValue && exactLineMap.TryGetValue(targetLine.Value, out bool covered))
                    {
                        isCovered = covered;
                    }
                }

                // If not found, try partial match (contains)
                if (!isCovered.HasValue)
                {
                    var matchKey = coverageMap.Keys.FirstOrDefault(k => k.Contains(targetClassName));
                    if (matchKey != null)
                    {
                        var partialLineMap = coverageMap[matchKey]; // Renamed from lineMap to avoid conflict
                        var targetLine = failure.Localization.PrimaryLineNumber;
                        if (targetLine.HasValue && partialLineMap.TryGetValue(targetLine.Value, out bool covered))
                        {
                            isCovered = covered;
                        }
                    }
                }
            }

            // Add metadata
            if (failure.Metadata == null) failure.Metadata = new Dictionary<string, string>();

            if (isCovered.HasValue)
            {
                failure.Metadata["CoverageStatus"] = isCovered.Value ? "Covered" : "NotCovered";
                failure.Metadata["CoverageInsight"] = isCovered.Value
                    ? "Failure occurred in code executed by tests (Logic Error)."
                    : "Failure occurred in code NOT executed by tests (Missing Test Scenario).";
            }
            else
            {
                failure.Metadata["CoverageStatus"] = "Unknown";
                failure.Metadata["CoverageInsight"] = "Could not correlate failure location with coverage data.";
            }
        }
    }
}