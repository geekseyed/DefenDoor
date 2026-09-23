using System;
using System.IO;
using System.Threading.Tasks;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class RoslynWorkspaceServiceTests : IDisposable
{
    private readonly RoslynWorkspaceService _service = new();

    private const string SampleSource = @"
using System;

namespace Sample
{
    public class Calculator
    {
        public int Add(int a, int b) => a + b;
    }
}";

    [Fact]
    public void CreateAdhocWorkspace_ReturnsEmptyWorkspace()
    {
        var ws = _service.CreateAdhocWorkspace();

        ws.Should().NotBeNull();
        ws.CurrentSolution.Projects.Should().BeEmpty();
    }

    [Fact]
    public void GetWorkspaceSnapshot_AfterInMemoryProject_ShowsProjectAndDocuments()
    {
        var ws = _service.CreateAdhocWorkspace();
        _service.AddInMemoryCSharpProject(ws, "SampleProj", ("Calculator.cs", SampleSource));

        var snapshot = _service.GetWorkspaceSnapshot();

        snapshot.Kind.Should().Be(RoslynWorkspaceKind.Adhoc);
        snapshot.ProjectCount.Should().Be(1);
        snapshot.Projects[0].Name.Should().Be("SampleProj");
        snapshot.Projects[0].DocumentCount.Should().Be(1);
        snapshot.Projects[0].DocumentNames.Should().Contain("Calculator.cs");
    }

    [Fact]
    public async Task GetCompilationSnapshot_ReturnsAssemblyNameAndSyntaxTrees()
    {
        var ws = _service.CreateAdhocWorkspace();
        var project = _service.AddInMemoryCSharpProject(ws, "SampleProj", ("Calculator.cs", SampleSource));

        var snapshot = await _service.GetCompilationSnapshotAsync(project);

        snapshot.AssemblyName.Should().Be("SampleProj");
        snapshot.SyntaxTreeCount.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task LoadSolutionAsync_MissingSolutionFile_ThrowsFileNotFound()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.sln");

        Func<Task> act = async () => await _service.LoadSolutionAsync(missing);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task Roslyn_ParsesRealRepositorySourceFile()
    {
        var repoRoot = FindRepoRoot();
        repoRoot.Should().NotBeNull("ISCM.sln must be discoverable from the test output directory");

        var target = Path.Combine(repoRoot!, "ISCM.Domain", "Entities", "Evidence.cs");
        File.Exists(target).Should().BeTrue($"expected real file at {target}");

        var ws = _service.CreateAdhocWorkspace();
        var project = _service.AddInMemoryCSharpProject(
            ws, "RealSmoke", ("Evidence.cs", await File.ReadAllTextAsync(target)));

        var snapshot = await _service.GetCompilationSnapshotAsync(project);

        snapshot.SyntaxTreeCount.Should().Be(1);
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && dir.GetFiles("ISCM.sln").Length == 0)
            dir = dir.Parent;
        return dir?.FullName;
    }

    public void Dispose() => _service.Dispose();
}