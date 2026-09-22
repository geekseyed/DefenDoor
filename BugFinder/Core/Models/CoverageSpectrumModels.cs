using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-12.3: Coverage Spectrum Models
/// Bridges the gap between raw coverage data and SBFL algorithms.
/// </summary>

/// <summary>
/// Raw coverage input for a single test case.
/// Typically parsed from Coverlet JSON output.
/// </summary>
public class TestCoverageInput
{
    public string TestName { get; set; } = string.Empty;
    public bool IsPassed { get; set; } // True = Pass, False = Fail
    public List<string> CoveredLines { get; set; } = new(); // Format: "File.cs:10"
}

/// <summary>
/// Intermediate model to hold the 4 counters for a specific line/method during construction.
/// </summary>
public class SpectrumBuilderContext
{
    public string ElementId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? MethodName { get; set; }
    public int? LineNumber { get; set; }

    // Counters being built
    public int PassedCount { get; set; }      // aep: Executed & Passed
    public int FailedCount { get; set; }      // aef: Executed & Failed
    public int PassSkipCount { get; set; }    // anp: Not Executed & Passed
    public int FailSkipCount { get; set; }    // anf: Not Executed & Failed
}

/// <summary>
/// Configuration for how granular the spectrum should be.
/// </summary>
public enum SpectrumGranularity
{
    LineLevel,   // Most precise, high memory usage
    MethodLevel, // Balanced
    ClassLevel   // Least precise, low memory usage
}