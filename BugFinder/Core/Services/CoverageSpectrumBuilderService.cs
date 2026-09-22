using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-12.3: Coverage Spectrum Builder Service
/// Transforms raw test coverage data into ExecutionSpectra required for SBFL.
/// Implements the logic to calculate aep, aef, anp, anf counters.
/// </summary>
public class CoverageSpectrumBuilderService
{
    /// <summary>
    /// BF-12.3 - Main Entry Point:
    /// Aggregates individual test coverage inputs into a full Execution Spectrum.
    /// </summary>
    /// <param name="allTestCoverages">List of coverage data for every test (Pass & Fail)</param>
    /// <param name="granularity">Level of detail (Line vs Method)</param>
    public List<ExecutionSpectrum> BuildSpectrum(
        List<TestCoverageInput> allTestCoverages,
        SpectrumGranularity granularity = SpectrumGranularity.LineLevel)
    {
        if (allTestCoverages == null || !allTestCoverages.Any())
        {
            return new List<ExecutionSpectrum>();
        }

        var totalPassed = allTestCoverages.Count(t => t.IsPassed);
        var totalFailed = allTestCoverages.Count(t => !t.IsPassed);

        // Dictionary to hold context for each unique element (File:Line)
        var elementContexts = new Dictionary<string, SpectrumBuilderContext>();

        // 1. Initialize Contexts based on ALL possible lines found in coverage
        // We need to know the universe of lines first to calculate 'Skipped' counts accurately
        var allUniqueElements = new HashSet<string>();

        foreach (var test in allTestCoverages)
        {
            foreach (var coveredLine in test.CoveredLines)
            {
                if (!allUniqueElements.Contains(coveredLine))
                {
                    allUniqueElements.Add(coveredLine);

                    var parts = coveredLine.Split(':');
                    var filePath = parts.Length > 0 ? parts[0] : "Unknown";
                    var lineNum = parts.Length > 1 && int.TryParse(parts[1], out var ln) ? ln : (int?)null;

                    elementContexts[coveredLine] = new SpectrumBuilderContext
                    {
                        ElementId = coveredLine,
                        FilePath = filePath,
                        LineNumber = lineNum,
                        // MethodName would require parsing method boundaries, skipped for LineLevel simplicity
                    };
                }
            }
        }

        // 2. Iterate through tests and increment counters
        foreach (var test in allTestCoverages)
        {
            var isPass = test.IsPassed;
            var coveredSet = new HashSet<string>(test.CoveredLines);

            foreach (var kvp in elementContexts)
            {
                var context = kvp.Value;
                bool isExecuted = coveredSet.Contains(kvp.Key);

                if (isExecuted)
                {
                    if (isPass) context.PassedCount++;
                    else context.FailedCount++;
                }
                else
                {
                    if (isPass) context.PassSkipCount++;
                    else context.FailSkipCount++;
                }
            }
        }

        // 3. Convert Contexts to Final ExecutionSpectrum models
        return elementContexts.Values.Select(ctx => new ExecutionSpectrum
        {
            ElementId = ctx.ElementId,
            FilePath = ctx.FilePath,
            MethodName = ctx.MethodName,
            LineNumber = ctx.LineNumber,
            PassedCount = ctx.PassedCount,
            FailedCount = ctx.FailedCount,
            PassSkipCount = ctx.PassSkipCount,
            FailSkipCount = ctx.FailSkipCount
        }).ToList();
    }
}