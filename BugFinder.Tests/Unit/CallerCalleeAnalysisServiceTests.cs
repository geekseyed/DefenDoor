using System;
using System.Linq;
using System.Threading.Tasks;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class CallerCalleeAnalysisServiceTests : IDisposable
{
    private readonly CallerCalleeAnalysisService _service = new();
    private readonly RoslynWorkspaceService _workspace = new();

    private const string LibrarySource = @"
namespace Lib
{
    public class Calculator
    {
        public int Add(int a, int b) => a + b;

        public int Twice(int x) => Add(x, x);

        public int Run(int a, int b)
        {
            var s = Add(a, b);
            return Twice(s);
        }
    }

    public static class Logger
    {
        public static void Log(string message)
        {
        }
    }
}";

    private const string CallerSource = @"
namespace App
{
    public class Caller
    {
        public int Run()
        {
            var calc = new Lib.Calculator();
            return calc.Add(2, 3);
        }
    }
}";

    private const string BrokenSource = @"
namespace Broken
{
    public class Weird
    {
        public void Call()
        {
            UndefinedHelper.Foo();
        }
    }
}";

    private Microsoft.CodeAnalysis.Project CreateProject(
        string name, params (string Name, string Source)[] files)
    {
        var ws = _workspace.CreateAdhocWorkspace();
        return _workspace.AddInMemoryCSharpProject(ws, name, files);
    }

    // Stage 3 — intra-class edges (expression-bodied + block body)
    [Fact]
    public async Task AnalyzeDocument_IntraClassCalls_ProduceResolvedEdges()
    {
        var project = CreateProject("Lib", ("Calculator.cs", LibrarySource));

        var evidence = await _service.AnalyzeDocumentAsync(project.Documents.First());

        evidence.DeclaredMethodCount.Should().Be(4); // Add, Twice, Run
        evidence.ResolvedEdgeCount.Should().Be(3);   // Twice→Add, Run→Add, Run→Twice

        var runEdge = evidence.Edges.First(e => e.CallerDisplayName.Contains("Run")
                                                && e.CalleeDisplayName.Contains("Add"));
        runEdge.IsResolved.Should().BeTrue();
        runEdge.CallerKey.Should().Contain("Lib.Calculator.Run(int,int)");
        runEdge.CalleeKey.Should().Contain("Lib.Calculator.Add(int,int)");
    }

    // Stage 3 — cross-class call across documents
    [Fact]
    public async Task AnalyzeProject_CrossClassCall_ResolvesAcrossDocuments()
    {
        var project = CreateProject("App",
            ("Calculator.cs", LibrarySource), ("Caller.cs", CallerSource));

        var report = await _service.AnalyzeProjectAsync(project);

        var crossEdge = report.AllEdges.First(e =>
            e.CallerKey!.Contains("App.Caller.Run()"));
        crossEdge.IsResolved.Should().BeTrue();
        crossEdge.CalleeKey.Should().Contain("Lib.Calculator.Add(int,int)");
    }

    // Core principle — unresolved callee = UNKNOWN edge
    [Fact]
    public async Task AnalyzeDocument_UnresolvedCall_RecordedAsUnknownEdge()
    {
        var project = CreateProject("Broken", ("Weird.cs", BrokenSource));

        var evidence = await _service.AnalyzeDocumentAsync(project.Documents.First());

        evidence.Edges.Should().HaveCount(1);
        evidence.UnresolvedEdgeCount.Should().Be(1);
        evidence.Edges[0].IsResolved.Should().BeFalse();
        evidence.Edges[0].CalleeKey.Should().BeNull();
        evidence.Edges[0].CalleeDisplayName.Should().Contain("UndefinedHelper.Foo");
    }

    // Stage 4 — fan-in: distinct callers of Add = Twice + Run
    [Fact]
    public async Task ComputeMetrics_FanIn_CountsDistinctCallers()
    {
        var project = CreateProject("Lib", ("Calculator.cs", LibrarySource));
        var report = await _service.AnalyzeProjectAsync(project);

        var addMetrics = report.Metrics.First(m => m.SymbolKey.Contains("Lib.Calculator.Add(int,int)"));

        addMetrics.FanIn.Should().Be(2);   // Twice, Run
        addMetrics.Callers.Distinct().Should().HaveCount(2);
    }

    // Stage 4 — fan-out: distinct callees of Run = Add + Twice
    [Fact]
    public async Task ComputeMetrics_FanOut_CountsDistinctCallees()
    {
        var project = CreateProject("Lib", ("Calculator.cs", LibrarySource));
        var report = await _service.AnalyzeProjectAsync(project);

        var runMetrics = report.Metrics.First(m => m.SymbolKey.Contains("Lib.Calculator.Run(int,int)"));

        runMetrics.FanOut.Should().Be(2);  // Add, Twice
        runMetrics.Callees.Distinct().Should().HaveCount(2);
    }

    // Stage 4 — full aggregation across three documents
    [Fact]
    public async Task AnalyzeProject_AggregatesEdgesAndMetrics()
    {
        var project = CreateProject("All",
            ("Calculator.cs", LibrarySource),
            ("Caller.cs", CallerSource),
            ("Weird.cs", BrokenSource));

        var report = await _service.AnalyzeProjectAsync(project);

        report.TotalDeclaredMethods.Should().Be(6); // Add, Twice, Run, Caller.Run, Weird.Call
        report.TotalEdges.Should().Be(5);           // 3 (lib) + 1 (caller) + 1 (broken)
        report.TotalResolvedEdges.Should().Be(4);
        report.TotalUnresolvedEdges.Should().Be(1);
        report.Metrics.Should().NotBeEmpty();
    }

    public void Dispose() => _workspace.Dispose();
}