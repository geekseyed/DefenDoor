using System;
using System.Collections.Generic;
using System.Linq;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class MutationFlTests
{
    private readonly MutationGenerationService _generation = new();
    private readonly SimulatedMutantExecutionGateway _gateway = new();
    private readonly MutationSuspiciousnessService _suspiciousness = new();

    // Detect scenario: T1 failed, T2 passed
    private static MutationScenario DetectScenario() => new()
    {
        ScenarioName = "Detect",
        Tests = new List<ResearchTestOutcome>
        {
            new() { TestId = "T1", Failed = true },
            new() { TestId = "T2", Failed = false }
        },
        Elements = new List<ElementExecution>
        {
            new() { ElementId = "E1", FilePath = "Calc.cs", ExecutingTestIds = new List<string> { "T1", "T2" } },
            new() { ElementId = "E2", FilePath = "Calc.cs", ExecutingTestIds = new List<string> { "T2" } }
        },
        Operators = new List<MutationOperatorKind>
        {
            MutationOperatorKind.StatementDeletion, MutationOperatorKind.ConstantReplacement
        }
    };

    // Ranking scenario: TF=2 (T1,T2), TP=2 (T3,T4)
    private static MutationScenario RankingScenario() => new()
    {
        ScenarioName = "Ranking",
        Tests = new List<ResearchTestOutcome>
        {
            new() { TestId = "T1", Failed = true },
            new() { TestId = "T2", Failed = true },
            new() { TestId = "T3", Failed = false },
            new() { TestId = "T4", Failed = false }
        },
        Elements = new List<ElementExecution>
        {
            new() { ElementId = "E1", FilePath = "Calc.cs", ExecutingTestIds = new List<string> { "T1", "T2", "T3" } },
            new() { ElementId = "E2", FilePath = "Calc.cs", ExecutingTestIds = new List<string> { "T1", "T4" } },
            new() { ElementId = "E3", FilePath = "Calc.cs", ExecutingTestIds = new List<string> { "T3" } }
        },
        Operators = new List<MutationOperatorKind>
        {
            MutationOperatorKind.StatementDeletion, MutationOperatorKind.ConstantReplacement
        }
    };

    // Stage 1 — operator catalog
    [Fact]
    public void OperatorCatalog_AllSixOperatorsDescribed()
    {
        var kinds = Enum.GetValues<MutationOperatorKind>();
        kinds.Should().HaveCount(6);
        foreach (var kind in kinds)
            MutationOperatorCatalog.Describe(kind).Should().NotBeNullOrWhiteSpace();
        MutationOperatorCatalog.Describe(MutationOperatorKind.StatementDeletion)
            .Should().Contain("remove");
    }

    // Stage 2 — deterministic plan
    [Fact]
    public void GeneratePlan_CartesianDeterministic()
    {
        var plan = _generation.GeneratePlan(DetectScenario());

        plan.Should().HaveCount(4);   // 2 elements x 2 operators
        plan.Select(p => p.MutantId).Should().ContainInOrder("MUT-001", "MUT-002", "MUT-003", "MUT-004");
        plan[0].ElementId.Should().Be("E1");
        plan[0].Operator.Should().Be(MutationOperatorKind.StatementDeletion);
        plan[0].Description.Should().Contain("StatementDeletion");
        plan.All(p => p.FilePath == "Calc.cs").Should().BeTrue();
    }

    [Fact]
    public void GeneratePlan_EmptyScenario_ReturnsEmpty()
    {
        _generation.GeneratePlan(new MutationScenario()).Should().BeEmpty();
    }

    // Spectrum bridge — aggregates match hand computation
    [Fact]
    public void DeriveSpectrum_AggregatesMatchHandComputation()
    {
        var spectrum = _generation.DeriveSpectrum(DetectScenario());

        var e1 = spectrum.First(s => s.ElementId == "E1");
        e1.FailedCount.Should().Be(1);
        e1.PassedCount.Should().Be(1);
        e1.FailSkipCount.Should().Be(0);
        e1.PassSkipCount.Should().Be(0);

        var e2 = spectrum.First(s => s.ElementId == "E2");
        e2.FailedCount.Should().Be(0);
        e2.PassedCount.Should().Be(1);
        e2.FailSkipCount.Should().Be(1);
        e2.PassSkipCount.Should().Be(0);
    }

    // Stage 3 — simulated semantics: StatementDeletion kills all executing
    [Fact]
    public void SimulatedGateway_StatementDeletion_KillsAllExecuting()
    {
        var plan = _generation.GeneratePlan(DetectScenario());
        var matrix = _gateway.Execute(DetectScenario(), plan);

        matrix.Source.Should().Be(MutationExecutionSource.Simulated);
        var sd = matrix.Entries.First(e => e.Candidate.MutantId == "MUT-001");
        sd.KilledByTestIds.Should().ContainInOrder("T1", "T2");
        sd.IsKilled.Should().BeTrue();
    }

    // Stage 3 — value-level operators kill failing executing tests only
    [Fact]
    public void SimulatedGateway_ValueOperators_KillFailedExecutingOnly()
    {
        var plan = _generation.GeneratePlan(DetectScenario());
        var matrix = _gateway.Execute(DetectScenario(), plan);

        var cr1 = matrix.Entries.First(e => e.Candidate.MutantId == "MUT-002"); // E1, exec {T1,T2}
        cr1.KilledByTestIds.Should().ContainInOrder("T1");                      // only the failed one

        var cr2 = matrix.Entries.First(e => e.Candidate.MutantId == "MUT-004"); // E2, exec {T2} passed
        cr2.KilledByTestIds.Should().BeEmpty();
        cr2.IsKilled.Should().BeFalse();
    }

    // Stage 4 — detection summary
    [Fact]
    public void DetectSurvivors_SummaryAndKillRate()
    {
        var plan = _generation.GeneratePlan(DetectScenario());
        var matrix = _gateway.Execute(DetectScenario(), plan);

        var summary = MutantDetectionService.Detect(matrix);

        summary.TotalMutants.Should().Be(4);
        summary.KilledCount.Should().Be(3);
        summary.SurvivedCount.Should().Be(1);
        summary.KillRate.Should().BeApproximately(0.75, 0.001);
        summary.SurvivorMutantIds.Should().ContainSingle().Which.Should().Be("MUT-004");
    }

    // Stage 5 — Metallaxis max-Ochiai, hand-computed ranking
    [Fact]
    public void Compute_MetallaxisMax_HandComputedRanking()
    {
        var scenario = RankingScenario();
        var plan = _generation.GeneratePlan(scenario);
        var matrix = _gateway.Execute(scenario, plan);

        var result = _suspiciousness.Compute(matrix);

        result.AlgorithmName.Should().Be("Metallaxis(simulated)");
        result.TotalElements.Should().Be(3);

        // E1: CR kills {T1,T2} -> 2/sqrt(2*2) = 1.0
        result.Entries[0].ElementId.Should().Be("E1");
        result.Entries[0].Score.Should().BeApproximately(1.0, 0.001);
        result.Entries[0].FilePath.Should().Be("Calc.cs");

        // E2: CR kills {T1} -> 1/sqrt(2*1) = 0.7071
        result.Entries[1].ElementId.Should().Be("E2");
        result.Entries[1].Score.Should().BeApproximately(0.7071, 0.001);

        // E3: killed only by passing T3 -> 0
        result.Entries[2].ElementId.Should().Be("E3");
        result.Entries[2].Score.Should().Be(0.0);

        result.Entries.Select(e => e.Rank).Should().ContainInOrder(1, 2, 3);
        MutationSuspiciousnessService.ResearchBoundaryNote.Should().Contain("never applied");
    }

    // Degenerate — green run: TF=0 -> all scores zero, no division errors
    [Fact]
    public void Compute_GreenRun_AllScoresZero()
    {
        var scenario = new MutationScenario
        {
            ScenarioName = "Green",
            Tests = new List<ResearchTestOutcome>
            {
                new() { TestId = "T1", Failed = false },
                new() { TestId = "T2", Failed = false }
            },
            Elements = new List<ElementExecution>
            {
                new() { ElementId = "X", FilePath = "Calc.cs", ExecutingTestIds = new List<string> { "T1", "T2" } }
            },
            Operators = new List<MutationOperatorKind> { MutationOperatorKind.StatementDeletion }
        };
        var plan = _generation.GeneratePlan(scenario);
        var matrix = _gateway.Execute(scenario, plan);

        var result = _suspiciousness.Compute(matrix);

        result.Entries.Should().ContainSingle().Which.Score.Should().Be(0.0);
    }

    [Fact]
    public void Compute_NullOrEmptyMatrix_EmptyResultNoThrow()
    {
        _suspiciousness.Compute(null).Entries.Should().BeEmpty();
        _suspiciousness.Compute(new MutationKillMatrix()).Entries.Should().BeEmpty();
    }
}