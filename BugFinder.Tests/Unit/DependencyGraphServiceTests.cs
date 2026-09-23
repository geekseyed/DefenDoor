using System;
using System.Linq;
using System.Threading.Tasks;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class DependencyGraphServiceTests : IDisposable
{
    private readonly DependencyGraphService _service = new();
    private readonly RoslynWorkspaceService _workspace = new();

    private const string LibSource = @"
namespace Lib
{
    public class Calculator
    {
        public int Add(int a, int b) => a + b;
    }
}";

    private const string AppSource = @"
namespace App
{
    public class Caller
    {
        private Lib.Calculator _calculator;

        public int Run()
        {
            return _calculator.Add(2, 3);
        }
    }
}";

    private const string GraphSource = @"
namespace Graph
{
    public class BaseEntity
    {
        public int Id { get; set; }
    }

    public class Helper
    {
        public int Value => 42;
    }

    public static class Logger
    {
        public static void Log(string message)
        {
        }
    }

    public class BaseService
    {
    }

    public class ChildService : BaseService
    {
        private BaseEntity _entity;

        public BaseEntity Find(int id)
        {
            return new BaseEntity();
        }

        public void Save(BaseEntity entity)
        {
        }

        public int Compute()
        {
            var calc = new Helper();
            Logger.Log(""working"");
            return calc.Value;
        }
    }
}";

    private const string MiniSource = @"
namespace Mini
{
    public class Hub { }
    public class A { private Hub _hub; }
    public class B { public Hub Get() { return new Hub(); } }
}";

    private const string BrokenSource = @"
namespace Broken
{
    public class Store
    {
        public void Save(MissingEntity entity)
        {
        }
    }
}";

    private Microsoft.CodeAnalysis.Project CreateProject(
        string name, params (string Name, string Source)[] files)
    {
        var ws = _workspace.CreateAdhocWorkspace();
        return _workspace.AddInMemoryCSharpProject(ws, name, files);
    }

    // Stage 1 — project dependencies
    [Fact]
    public void CollectProjectDependencies_DetectsEdges_AndEmptyForSingleProject()
    {
        var ws = _workspace.CreateAdhocWorkspace();
        var lib = _workspace.AddInMemoryCSharpProject(ws, "Lib", ("Calculator.cs", LibSource));
        var app = _workspace.AddInMemoryCSharpProjectWithReferences(
             ws, "App", new[] { new ProjectReference(lib.Id) }, ("Caller.cs", AppSource));

        var edges = _service.CollectProjectDependencies(app);
        edges.Should().HaveCount(1);
        edges[0].FromProject.Should().Be("App");
        edges[0].ToProject.Should().Be("Lib");

        _service.CollectProjectDependencies(lib).Should().BeEmpty();
    }

    // Stage 2 — inheritance + field
    [Fact]
    public async Task TypeDependencies_InheritanceAndField_Detected()
    {
        var report = await _service.AnalyzeProjectAsync(CreateProject("GraphProj", ("Graph.cs", GraphSource)));

        report.TypeEdges.Should().Contain(e =>
            e.FromTypeName.Contains("ChildService")
            && e.ToTypeName!.Contains("BaseService")
            && e.Kind == TypeDependencyKind.Inheritance
            && e.IsResolved);

        report.TypeEdges.Should().Contain(e =>
            e.FromTypeName.Contains("ChildService")
            && e.ToTypeName!.Contains("BaseEntity")
            && e.Kind == TypeDependencyKind.FieldType);
    }

    // Stage 2 — parameter / return / instantiation / static access
    [Fact]
    public async Task TypeDependencies_ParameterReturnInstantiationStaticAccess_Detected()
    {
        var report = await _service.AnalyzeProjectAsync(CreateProject("GraphProj", ("Graph.cs", GraphSource)));

        var toBaseEntity = report.TypeEdges
            .Where(e => e.FromTypeName.Contains("ChildService")
                        && e.ToTypeName!.Contains("BaseEntity"))
            .ToList();

        toBaseEntity.Should().HaveCount(4);
        toBaseEntity.Select(x => x.Kind).Should().Contain(new[]
        {
            TypeDependencyKind.ReturnType, TypeDependencyKind.ParameterType,
            TypeDependencyKind.Instantiation, TypeDependencyKind.FieldType
        });

        report.TypeEdges.Should().Contain(e =>
            e.FromTypeName.Contains("ChildService")
            && e.ToTypeName!.Contains("Helper")
            && e.Kind == TypeDependencyKind.Instantiation);

        report.TypeEdges.Should().Contain(e =>
            e.FromTypeName.Contains("ChildService")
            && e.ToTypeName!.Contains("Logger")
            && e.Kind == TypeDependencyKind.StaticAccess);
    }

    // Stage 3 — method-level edges with stable keys
    [Fact]
    public async Task MethodDependencies_EdgesWithStableKeys()
    {
        var report = await _service.AnalyzeProjectAsync(CreateProject("GraphProj", ("Graph.cs", GraphSource)));

        report.MethodEdges.Should().HaveCount(3); // Find→BaseEntity, Compute→Helper, Compute→Logger

        report.MethodEdges.Should().Contain(e =>
            e.FromMethodKey.Contains("ChildService.Find(int)")
            && e.Kind == TypeDependencyKind.Instantiation
            && e.ToTypeKey!.Contains("BaseEntity"));

        report.MethodEdges.Should().Contain(e =>
            e.FromMethodKey.Contains("ChildService.Compute()")
            && e.Kind == TypeDependencyKind.StaticAccess
            && e.ToTypeKey!.Contains("Logger"));
    }

    // Core principle — unresolved type reference = UNKNOWN edge
    [Fact]
    public async Task UnresolvedTypeDependency_RecordedAsUnknown()
    {
        var report = await _service.AnalyzeProjectAsync(CreateProject("BrokenProj", ("Store.cs", BrokenSource)));

        var unresolved = report.TypeEdges.Where(e => !e.IsResolved).ToList();
        unresolved.Should().HaveCount(1);
        unresolved[0].Kind.Should().Be(TypeDependencyKind.ParameterType);
        unresolved[0].ToTypeKey.Should().BeNull();
        unresolved[0].ToTypeName.Should().Be("MissingEntity");
        report.TotalUnresolvedTypeEdges.Should().Be(1);
    }

    // Stage 4 — graph nodes with fan metrics
    [Fact]
    public async Task AnalyzeProject_BuildsGraphNodes_WithFanMetrics()
    {
        var report = await _service.AnalyzeProjectAsync(CreateProject("MiniProj", ("Mini.cs", MiniSource)));

        // Type edges: A→Hub (Field), B→Hub (Return + Instantiation)
        report.TotalTypeEdges.Should().Be(3);
        report.TotalMethodEdges.Should().Be(1); // B.Get() → Hub (Instantiation)

        var hub = report.Nodes.First(n => n.Level == "Type" && n.DisplayName.Contains("Hub"));
        hub.FanIn.Should().Be(3);   // distinct sources: A, B
        hub.FanOut.Should().Be(0);

        var a = report.Nodes.First(n => n.Level == "Type" && n.DisplayName.Contains("Mini.A"));
        a.FanOut.Should().Be(1);

        report.Nodes.Should().HaveCount(4); // 3 types + 1 method
        report.Nodes.Should().Contain(n => n.Level == "Method" && n.DisplayName.Contains("B.Get()"));
    }

    public void Dispose() => _workspace.Dispose();
}