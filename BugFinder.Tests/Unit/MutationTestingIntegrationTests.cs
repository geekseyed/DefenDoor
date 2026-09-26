using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class MutationTestingIntegrationTests
{
    private readonly MutationGenerationService _generation = new();
    private readonly SimulatedMutantExecutionGateway _gateway = new();
    private readonly MutationTestingIntegrationService _service = new();

    // T1 failed; T2, T9, T0 passed. E1 exec {T1,T2}; E3 exec {T9}.
    // Operators: ConstantReplacement ONLY.
    // Why CR-only: StatementDeletion kills ALL executing tests, so with SD
    // in the plan no test can ever be weak - the weak-test narrative
    // requires value-level operators only.
    // Plan (2 elements x 1 operator):
    //   MUT-001 (E1, CR) -> killed by {T1} (the only failing executing test)
    //   MUT-002 (E3, CR) -> survivor (E3 executes only the passing T9)
    private static MutationScenario WeakScenario() => new()
    {
        ScenarioName = "Weak",
        Tests = new List<ResearchTestOutcome>
        {
            new() { TestId = "T1", Failed = true },
            new() { TestId = "T2", Failed = false },
            new() { TestId = "T9", Failed = false },
            new() { TestId = "T0", Failed = false }
        },
        Elements = new List<ElementExecution>
        {
            new() { ElementId = "E1", FilePath = "Calc.cs", ExecutingTestIds = new List<string> { "T1", "T2" } },
            new() { ElementId = "E3", FilePath = "Calc.cs", ExecutingTestIds = new List<string> { "T9" } }
        },
        Operators = new List<MutationOperatorKind>
        {
            MutationOperatorKind.ConstantReplacement
        }
    };

    private (MutationScenario Scenario, MutationKillMatrix Matrix, MutationIntegrationReport Report) Arrange()
    {
        var scenario = WeakScenario();
        var matrix = _gateway.Execute(scenario, _generation.GeneratePlan(scenario));
        var report = _service.Integrate(scenario, matrix);
        return (scenario, matrix, report);
    }

    // Stage 1 — per-test powers, hand-computed, ordering killed-desc then TestId
    [Fact]
    public void Integrate_PerTestPowers_HandComputedAndOrdered()
    {
        var (_, _, report) = Arrange();

        report.ScenarioName.Should().Be("Weak");
        report.TestPowers.Should().HaveCount(4);

        // T1 killed 1/1; T0, T2, T9 killed 0 -> ordinal tie-break
        report.TestPowers.Select(p => p.TestId)
            .Should().ContainInOrder("T1", "T0", "T2", "T9");

        var t1 = report.TestPowers.First(p => p.TestId == "T1");
        t1.ExecutedMutantCount.Should().Be(1);
        t1.KilledMutantCount.Should().Be(1);
        t1.KillingRatio.Should().BeApproximately(1.0, 0.001);
        t1.IsWeak.Should().BeFalse();

        var t2 = report.TestPowers.First(p => p.TestId == "T2");
        t2.ExecutedMutantCount.Should().Be(1);
        t2.KilledMutantCount.Should().Be(0);
        t2.KillingRatio.Should().Be(0.0);
        t2.IsWeak.Should().BeTrue();
    }

    // Stage 3 — weak tests with executed mutant ids as leads
    [Fact]
    public void Integrate_WeakTestsDetected_WithExecutedMutantIds()
    {
        var (_, _, report) = Arrange();

        // T2 (executes MUT-001) and T9 (executes MUT-002) kill nothing;
        // T0 executes nothing -> not weak (never had a chance)
        report.WeakTests.Select(w => w.TestId).Should().ContainInOrder("T2", "T9");

        var t9 = report.WeakTests.First(w => w.TestId == "T9");
        t9.ExecutedMutantCount.Should().Be(1);
        t9.ExecutedMutantIds.Should().ContainSingle().Which.Should().Be("MUT-002");
        t9.Reason.Should().Contain("kills none");
    }

    // Stage 3 — a test executing nothing is NOT weak (never had a chance)
    [Fact]
    public void Integrate_TestExecutingNothing_NotWeak()
    {
        var (_, _, report) = Arrange();

        var t0 = report.TestPowers.First(p => p.TestId == "T0");
        t0.ExecutedMutantCount.Should().Be(0);
        t0.IsWeak.Should().BeFalse();
        report.WeakTests.Should().NotContain(w => w.TestId == "T0");
    }

    // Stage 2 — per-element scores
    [Fact]
    public void Integrate_ElementScores_HandComputed()
    {
        var (_, _, report) = Arrange();

        report.ElementScores.Should().HaveCount(2);
        var e1 = report.ElementScores.First(s => s.ElementId == "E1");
        e1.MutantCount.Should().Be(1);
        e1.KilledCount.Should().Be(1);
        e1.MutationScore.Should().BeApproximately(1.0, 0.001);

        var e3 = report.ElementScores.First(s => s.ElementId == "E3");
        e3.MutantCount.Should().Be(1);
        e3.KilledCount.Should().Be(0);
        e3.MutationScore.Should().Be(0.0);
    }

    // Stage 2 — overall score reuses BF-15.1 detection (single truth)
    [Fact]
    public void Integrate_OverallScore_MatchesDetectionSingleTruth()
    {
        var (_, matrix, report) = Arrange();

        var detection = MutantDetectionService.Detect(matrix);
        report.TotalMutants.Should().Be(2);
        report.KilledMutants.Should().Be(1);
        report.SurvivedMutants.Should().Be(1);
        report.OverallMutationScore.Should().BeApproximately(0.5, 0.001);
        report.OverallMutationScore.Should().Be(detection.KillRate);
        report.Source.Should().Be(MutationExecutionSource.Simulated);
    }

    // Graceful degradation — empty inputs, no throw
    [Fact]
    public void Integrate_EmptyInputs_EmptyReportNoThrow()
    {
        _service.Integrate(null, null).TestPowers.Should().BeEmpty();
        _service.Integrate(new MutationScenario(), null).TestPowers.Should().BeEmpty();
        _service.Integrate(new MutationScenario(), new MutationKillMatrix()).TestPowers.Should().BeEmpty();
    }

    // Integration sanity — killers always execute the candidate's element.
    // BeSubsetOf (not OnlyContain): survivor entries have EMPTY killer lists
    // and OnlyContain fails on empty collections by design.
    [Fact]
    public void Integrate_KillAttributionInvariant_KillersAlwaysExecuteElement()
    {
        var (scenario, matrix, _) = Arrange();

        foreach (var entry in matrix.Entries)
        {
            var element = scenario.Elements.First(e => e.ElementId == entry.Candidate.ElementId);
            entry.KilledByTestIds.Should().BeSubsetOf(element.ExecutingTestIds);
        }
    }

    // Stage 4 — JSON evidence carries the banner (15.7 lesson applied)
    [Fact]
    public void ToJson_PreservesEvidence_WithDisclaimer()
    {
        var (_, _, report) = Arrange();

        var json = _service.ToJson(report);

        json.Should().Contain("EXPERIMENTAL");
        json.Should().Contain("\"IsExperimental\": true");
        json.Should().Contain("WeakTests");
        json.Should().Contain("OverallMutationScore");
    }
}