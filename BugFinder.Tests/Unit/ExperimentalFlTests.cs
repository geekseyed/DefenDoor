using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class ExperimentalFlTests
{
    private static ExecutionSpectrum Spectrum(
        string id, int failed, int passed, int failSkip = 0, int passSkip = 0) => new()
        {
            ElementId = id,
            FilePath = "Calc.cs",
            FailedCount = failed,
            PassedCount = passed,
            FailSkipCount = failSkip,
            PassSkipCount = passSkip
        };

    // Stage 2 — hand-computed formulas
    [Fact]
    public void DStar_Formula_HandComputed()
    {
        // failed=3, passed=1, tf=3 -> 9/(1+3) = 2.25
        var result = new DStarLocalizer().Localize(new[] { Spectrum("E", 3, 1) });
        result.Entries[0].Score.Should().BeApproximately(2.25, 0.001);
    }

    [Fact]
    public void Op2_Formula_HandComputed()
    {
        // failed=3, passed=1, tp=4 -> 3 - 1/5 = 2.8
        var result = new Op2Localizer().Localize(new[] { Spectrum("E", 3, 1, passSkip: 3) });
        result.Entries[0].Score.Should().BeApproximately(2.8, 0.001);
    }

    [Fact]
    public void Wong3_ClampsAtZero_WhenPassedDominated()
    {
        var result = new Wong3Localizer().Localize(new[] { Spectrum("E", 1, 5) });
        result.Entries[0].Score.Should().Be(0.0);
    }

    [Fact]
    public void OchiaiReference_Formula_HandComputed()
    {
        // 3 / sqrt(3 * 4) = 0.8660
        var result = new OchiaiReferenceLocalizer().Localize(new[] { Spectrum("E", 3, 1) });
        result.Entries[0].Score.Should().BeApproximately(0.8660, 0.001);
    }

    // Stage 1 — degenerate input
    [Fact]
    public void Localize_EmptySpectra_EmptyResultNoThrow()
    {
        var result = new DStarLocalizer().Localize(Array.Empty<ExecutionSpectrum>());
        result.TotalElements.Should().Be(0);
        result.Entries.Should().BeEmpty();
    }

    // Deterministic tie-break
    [Fact]
    public void Localize_TieBreaks_ByElementId()
    {
        var spectra = new[]
        {
            Spectrum("Zeta",  0, 2, failSkip: 1),
            Spectrum("Alpha", 0, 1, failSkip: 1, passSkip: 1)
        };

        var result = new Wong3Localizer().Localize(spectra);

        result.Entries.Select(e => e.ElementId).Should().ContainInOrder("Alpha", "Zeta");
        result.Entries.Select(e => e.Rank).Should().ContainInOrder(1, 2);
    }

    // Stages 3-4 — default benchmark: all four algorithms pass all four scenarios
    [Fact]
    public void Compare_DefaultDataset_AllAlgorithmsPassAllScenarios()
    {
        var report = new ExperimentalComparisonService().Compare(null, null);

        report.Rows.Should().HaveCount(16);              // 4 scenarios x 4 algorithms
        report.Summaries.Should().HaveCount(4);
        report.Summaries.Should().OnlyContain(s => s.ScenariosPassed == s.TotalScenarios);
        report.Summaries.Should().Contain(s => s.AlgorithmName == "Ochiai(reference)");
    }

    // Stage 4 — a miss is recorded as evidence, never thrown
    [Fact]
    public void Compare_MissedExpectation_RecordedGracefully()
    {
        var scenario = new SbflBenchmarkScenario
        {
            Name = "Impossible",
            ExpectedTop1 = "Ghost",
            Spectra = new List<ExecutionSpectrum> { Spectrum("Real", 2, 0) }
        };

        var report = new ExperimentalComparisonService().Compare(
            new[] { scenario }, new IExperimentalFaultLocalizer[] { new DStarLocalizer() });

        report.Rows.Should().ContainSingle().Which.GroundTruthHit.Should().BeFalse();
        report.Summaries.Should().ContainSingle().Which.ScenariosPassed.Should().Be(0);
    }

    // Stage 5 — evidence preserved as JSON with the research disclaimer
    [Fact]
    public void ToJson_PreservesEvidence_WithDisclaimer()
    {
        var report = new ExperimentalComparisonService().Compare(null, null);
        var json = new ExperimentalComparisonService().ToJson(report);

        json.Should().Contain("EXPERIMENTAL");
        json.Should().Contain("DStar");
        json.Should().Contain("GroundTruthHit");
        json.Should().Contain("\"IsExperimental\": true");
    }
}