using System.Linq;
using System.Threading.Tasks;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class StaticRelationshipServiceTests : IDisposable
{
    private readonly StaticRelationshipService _service = new();
    private readonly RoslynWorkspaceService _workspace = new();

    private const string Source = @"
namespace Rel
{
    public class BaseService
    {
        public int Id { get; set; }
    }

    public class Engine
    {
        public int GetPower() => 100;
    }

    public static class Logger
    {
        public static void Log(string message)
        {
        }
    }

    public class ChildService : BaseService
    {
        private Engine _engine;

        public int Compute()
        {
            Logger.Log(""computing"");
            return _engine.GetPower();
        }
    }

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

    // Stage 5 — aggregate over the whole taxonomy
    [Fact]
    public async Task AnalyzeProject_ClassifiesAllRelationshipKinds()
    {
        var report = await _service.AnalyzeProjectAsync(CreateProject("RelProj", ("Rel.cs", Source)));

        report.TotalInheritance.Should().Be(1);   // ChildService : BaseService
        report.TotalComposition.Should().Be(2);   // ChildService._engine + BaseService.Id
        report.TotalInvocations.Should().Be(2);   // Compute → Logger.Log, Compute → Engine.GetPower
        report.TotalUses.Should().Be(8);          // 7 type-level + 1 method-level (Compute→Logger static)
        report.TotalRelationships.Should().Be(13);
        report.TotalUnresolved.Should().Be(1);    // MissingEntity
    }

    // Stage 2 — is-a
    [Fact]
    public async Task Inheritance_Relationship_IsA_WithStableKeys()
    {
        var report = await _service.AnalyzeProjectAsync(CreateProject("RelProj", ("Rel.cs", Source)));

        var rel = report.Inheritance.Should().ContainSingle().Subject;
        rel.FromDisplayName.Should().Contain("ChildService");
        rel.ToDisplayName.Should().Contain("BaseService");
        rel.FromKey.Should().StartWith("T|");
        rel.ToKey.Should().StartWith("T|");
        rel.IsResolved.Should().BeTrue();
    }

    // Stage 3 — has-a with provenance detail
    [Fact]
    public async Task Composition_Relationship_HasA_KeepsSourceKind()
    {
        var report = await _service.AnalyzeProjectAsync(CreateProject("RelProj", ("Rel.cs", Source)));

        report.Composition.Should().Contain(r =>
            r.FromDisplayName.Contains("ChildService")
            && r.ToDisplayName!.Contains("Engine")
            && r.Detail == "FieldType");

        report.Composition.Should().Contain(r =>
            r.FromDisplayName.Contains("BaseService")
            && r.ToDisplayName == "int"
            && r.Detail == "PropertyType");
    }

    // Stage 4 — method → method via call graph
    [Fact]
    public async Task Invocation_Relationships_MethodToMethod()
    {
        var report = await _service.AnalyzeProjectAsync(CreateProject("RelProj", ("Rel.cs", Source)));

        report.Invocations.Should().Contain(r =>
            r.FromKey.Contains("Rel.ChildService.Compute()")
            && r.ToKey!.Contains("Rel.Engine.GetPower()"));

        report.Invocations.Should().Contain(r =>
            r.FromKey.Contains("Rel.ChildService.Compute()")
            && r.ToKey!.Contains("Rel.Logger.Log(string)"));
    }

    // Core principle — unresolved reference = UNKNOWN relationship
    [Fact]
    public async Task UnresolvedType_RecordedAsUnresolvedUse()
    {
        var report = await _service.AnalyzeProjectAsync(CreateProject("RelProj", ("Rel.cs", Source)));

        var unresolved = report.Uses.Where(r => !r.IsResolved).ToList();
        unresolved.Should().HaveCount(1);
        unresolved[0].ToKey.Should().BeNull();
        unresolved[0].ToDisplayName.Should().Be("MissingEntity");
        unresolved[0].Detail.Should().Be("ParameterType");
        unresolved[0].FromKey.Should().Contain("Rel.Store");
    }

    // Stage filters + unique symbol registry
    [Fact]
    public async Task StageFilters_SplitByKind_AndRegistryExcludesUnknown()
    {
        var report = await _service.AnalyzeProjectAsync(CreateProject("RelProj", ("Rel.cs", Source)));

        StaticRelationshipService.ExtractInheritance(report.Relationships).Should().HaveCount(1);
        StaticRelationshipService.ExtractComposition(report.Relationships).Should().HaveCount(2);
        StaticRelationshipService.ExtractInvocation(report.Relationships).Should().HaveCount(2);

        report.UniqueSymbolKeys.Should().NotContain(k => k.StartsWith("UNKNOWN|"));
        report.UniqueSymbolKeys.Should().Contain(k => k.Contains("Rel.Engine.GetPower()"));
        report.UniqueSymbolKeys.Should().Contain(k => k.Contains("Rel.Engine"));
    }

    public void Dispose() => _workspace.Dispose();
}