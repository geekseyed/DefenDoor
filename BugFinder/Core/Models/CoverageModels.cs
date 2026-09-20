namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-06: Coverage Intelligence Models
/// </summary>
public class CoverageSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    public List<ModuleCoverage> Modules { get; set; } = new();

    // Summary statistics
    public int TotalLines { get; set; }
    public int CoveredLines { get; set; }
    public double CoveragePercentage { get; set; }

    public void CalculateSummary()
    {
        TotalLines = 0;
        CoveredLines = 0;

        foreach (var module in Modules)
        {
            foreach (var cls in module.Classes)
            {
                TotalLines += cls.TotalLines;
                CoveredLines += cls.CoveredLines;
            }
        }

        CoveragePercentage = TotalLines > 0 ? (double)CoveredLines / TotalLines * 100 : 0;
    }
}

public class ModuleCoverage
{
    public string AssemblyName { get; set; } = string.Empty;
    public List<ClassCoverage> Classes { get; set; } = new();
}

public class ClassCoverage
{
    public string ClassName { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public List<LineCoverage> Lines { get; set; } = new();

    // Cached stats
    public int TotalLines { get; set; }
    public int CoveredLines { get; set; }

    public void CalculateStats()
    {
        TotalLines = Lines.Count;
        CoveredLines = Lines.Count(l => l.IsCovered);
    }
}

public class LineCoverage
{
    public int LineNumber { get; set; }
    public bool IsCovered { get; set; } // Changed to settable
    public int HitCount { get; set; }
}