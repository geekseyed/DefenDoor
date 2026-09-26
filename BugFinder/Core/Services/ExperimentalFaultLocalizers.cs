using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-15.7 Stage 1: experimental algorithm interface over BF-12 spectra.
/// Stages 2: three established experimental formulas + an in-harness
/// Ochiai reference used ONLY for benchmarking (production SBFL = BF-12.4-12.6).
/// Deterministic ranking: score desc, then ElementId asc.
/// </summary>
public interface IExperimentalFaultLocalizer
{
    string AlgorithmName { get; }
    ExperimentalLocalizationResult Localize(IReadOnlyList<ExecutionSpectrum> spectra);
}

public abstract class SpectrumLocalizerBase : IExperimentalFaultLocalizer
{
    public abstract string AlgorithmName { get; }

    public ExperimentalLocalizationResult Localize(IReadOnlyList<ExecutionSpectrum> spectra)
    {
        var result = new ExperimentalLocalizationResult
        {
            AlgorithmName = AlgorithmName,
            TotalElements = spectra?.Count ?? 0
        };
        if (spectra is null || spectra.Count == 0) return result;

        // consistent test-run totals derivable from any row
        var totalFailed = spectra.Max(s => s.FailedCount + s.FailSkipCount);
        var totalPassed = spectra.Max(s => s.PassedCount + s.PassSkipCount);

        var entries = spectra.Select(s => new ExperimentalSuspiciousness
        {
            ElementId = s.ElementId,
            FilePath = s.FilePath,
            MethodName = s.MethodName,
            LineNumber = s.LineNumber,
            Score = Math.Max(0.0, ComputeScore(s.FailedCount, s.PassedCount, totalFailed, totalPassed))
        })
        .OrderByDescending(e => e.Score)
        .ThenBy(e => e.ElementId, StringComparer.Ordinal)
        .ToList();

        for (var i = 0; i < entries.Count; i++) entries[i].Rank = i + 1;
        result.Entries = entries;
        return result;
    }

    protected abstract double ComputeScore(int failed, int passed, int totalFailed, int totalPassed);
}

/// <summary>D* (DStar): failed^2 / (passed + totalFailed)</summary>
public class DStarLocalizer : SpectrumLocalizerBase
{
    public override string AlgorithmName => "DStar";
    protected override double ComputeScore(int failed, int passed, int totalFailed, int totalPassed)
        => failed == 0 ? 0.0 : (double)(failed * failed) / (passed + totalFailed);
}

/// <summary>Wong3: max(0, failed - 3*passed)</summary>
public class Wong3Localizer : SpectrumLocalizerBase
{
    public override string AlgorithmName => "Wong3";
    protected override double ComputeScore(int failed, int passed, int totalFailed, int totalPassed)
        => Math.Max(0.0, failed - 3.0 * passed);
}

/// <summary>Op2: failed - passed/(totalPassed + 1)</summary>
public class Op2Localizer : SpectrumLocalizerBase
{
    public override string AlgorithmName => "Op2";
    protected override double ComputeScore(int failed, int passed, int totalFailed, int totalPassed)
        => failed - (double)passed / (totalPassed + 1);   // clamped >= 0 by base
}

/// <summary>Ochiai reference for benchmark comparison only.</summary>
public class OchiaiReferenceLocalizer : SpectrumLocalizerBase
{
    public override string AlgorithmName => "Ochiai(reference)";
    protected override double ComputeScore(int failed, int passed, int totalFailed, int totalPassed)
        => totalFailed == 0 || failed + passed == 0
            ? 0.0
            : failed / Math.Sqrt((double)totalFailed * (failed + passed));
}