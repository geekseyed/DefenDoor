using System.Text.Json;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-06.1: Coverage Data Ingestion
/// Parses JSON coverage reports (Coverlet format) into domain models.
/// </summary>
public class CoverageReportParser
{
    public CoverageSession Parse(string jsonContent)
    {
        var session = new CoverageSession();

        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            return session;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            if (!root.TryGetProperty("Modules", out var modulesElement))
                return session;

            foreach (var moduleElem in modulesElement.EnumerateArray())
            {
                var assemblyName = moduleElem.GetProperty("Name").GetString() ?? "Unknown";
                var module = new ModuleCoverage { AssemblyName = assemblyName };

                if (moduleElem.TryGetProperty("Classes", out var classesElement))
                {
                    foreach (var classElem in classesElement.EnumerateArray())
                    {
                        var className = classElem.GetProperty("Name").GetString() ?? "Unknown";
                        var namespaceName = classElem.GetProperty("Namespace").GetString() ?? string.Empty;

                        var classCoverage = new ClassCoverage
                        {
                            ClassName = className,
                            Namespace = namespaceName,
                            Lines = new List<LineCoverage>()
                        };

                        if (classElem.TryGetProperty("Methods", out var methodsElement))
                        {
                            foreach (var methodElem in methodsElement.EnumerateArray())
                            {
                                if (methodElem.TryGetProperty("Lines", out var linesElement))
                                {
                                    foreach (var lineElem in linesElement.EnumerateArray())
                                    {
                                        var lineNum = lineElem.GetProperty("Line").GetInt32();
                                        var hits = lineElem.GetProperty("Hits").GetInt32();

                                        classCoverage.Lines.Add(new LineCoverage
                                        {
                                            LineNumber = lineNum,
                                            IsCovered = hits > 0,
                                            HitCount = hits
                                        });
                                    }
                                }
                            }
                        }

                        classCoverage.CalculateStats();
                        module.Classes.Add(classCoverage);
                    }
                }

                session.Modules.Add(module);
            }

            session.CalculateSummary();
        }
        catch (JsonException ex)
        {
            // Log error or handle malformed JSON gracefully
            throw new InvalidOperationException("Failed to parse coverage report JSON.", ex);
        }

        return session;
    }
}