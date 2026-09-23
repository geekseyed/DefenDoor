using System;
using System.Linq;
using System.Threading.Tasks;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class SymbolResolutionServiceTests : IDisposable
{
    private readonly SymbolResolutionService _service = new();
    private readonly RoslynWorkspaceService _workspace = new();

    private const string LibrarySource = @"
namespace Lib
{
    public class Calculator
    {
        public int Add(int a, int b) => a + b;

        public static string Version => ""1.0"";
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

    private static async Task<(SemanticModel Model, SyntaxNode Root)> LoadAsync(
        Microsoft.CodeAnalysis.Project project, string docName)
    {
        var doc = project.Documents.First(d => d.Name == docName);
        var model = (await doc.GetSemanticModelAsync())!;
        var root = (await doc.GetSyntaxTreeAsync())!.GetRoot();
        return (model, root);
    }

    // Stage 1
    [Fact]
    public async Task ResolveTypeSymbol_GivesStableIdentity()
    {
        var project = CreateProject("TestLib", ("Calculator.cs", LibrarySource));
        var (model, root) = await LoadAsync(project, "Calculator.cs");
        var classDecl = root.DescendantNodes().OfType<ClassDeclarationSyntax>().First();

        var evidence = _service.ResolveTypeSymbol(model, classDecl);

        evidence.IsResolved.Should().BeTrue();
        evidence.Kind.Should().Be(SemanticSymbolKind.Type);
        evidence.Identity!.FullyQualifiedName.Should().Be("Lib.Calculator");
        evidence.Identity.AssemblyName.Should().NotBeNullOrEmpty();
        evidence.Identity.ToStableKey().Should().StartWith("T|");
    }

    // Stage 2
    [Fact]
    public async Task ResolveMethodSymbol_IncludesParameterTypesInIdentity()
    {
        var project = CreateProject("TestLib", ("Calculator.cs", LibrarySource));
        var (model, root) = await LoadAsync(project, "Calculator.cs");
        var addDecl = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>().First(m => m.Identifier.Text == "Add");

        var evidence = _service.ResolveMethodSymbol(model, addDecl);

        evidence.IsResolved.Should().BeTrue();
        evidence.Identity!.FullyQualifiedName.Should().Be("Lib.Calculator.Add");
        evidence.Identity.Parameters.Should().Be("(int,int)");
        evidence.Identity.ToStableKey().Should().StartWith("M|");
        evidence.Identity.ToStableKey().Should().Contain("Lib.Calculator.Add(int,int)");
    }

    // Stage 3
    [Fact]
    public async Task ResolvePropertySymbol_StaticProperty_ResolvedWithIdentity()
    {
        var project = CreateProject("TestLib", ("Calculator.cs", LibrarySource));
        var (model, root) = await LoadAsync(project, "Calculator.cs");
        var propDecl = root.DescendantNodes()
            .OfType<PropertyDeclarationSyntax>().First(p => p.Identifier.Text == "Version");

        var evidence = _service.ResolvePropertySymbol(model, propDecl);

        evidence.IsResolved.Should().BeTrue();
        evidence.IsStatic.Should().BeTrue();
        evidence.Identity!.FullyQualifiedName.Should().Be("Lib.Calculator.Version");
        evidence.Identity.ToStableKey().Should().StartWith("P|");
    }

    // Stage 4 — THE identity test: same symbol from two syntax locations → one key
    [Fact]
    public async Task SameSymbol_FromDeclarationAndFromInvocation_HasSameStableKey()
    {
        var project = CreateProject("TestLib",
            ("Calculator.cs", LibrarySource), ("Caller.cs", CallerSource));

        var (model1, root1) = await LoadAsync(project, "Calculator.cs");
        var addDecl = root1.DescendantNodes()
            .OfType<MethodDeclarationSyntax>().First(m => m.Identifier.Text == "Add");
        var declared = _service.ResolveMethodSymbol(model1, addDecl);

        var (model2, root2) = await LoadAsync(project, "Caller.cs");
        var invocation = root2.DescendantNodes()
            .OfType<InvocationExpressionSyntax>().First(i => i.ToString().Contains("calc.Add"));
        var invoked = _service.ResolveInvokedSymbol(model2, invocation);

        declared.IsResolved.Should().BeTrue();
        invoked.IsResolved.Should().BeTrue();
        invoked.Identity!.ToStableKey().Should().Be(declared.Identity!.ToStableKey());
    }

    // Core principle — unresolved = UNKNOWN, not failure
    [Fact]
    public async Task ResolveInvokedSymbol_Unresolved_RecordedAsUnknown()
    {
        var project = CreateProject("Broken", ("Weird.cs", BrokenSource));
        var (model, root) = await LoadAsync(project, "Weird.cs");
        var invocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().First();

        var evidence = _service.ResolveInvokedSymbol(model, invocation);

        evidence.IsResolved.Should().BeFalse();
        evidence.Kind.Should().Be(SemanticSymbolKind.Unknown);
        evidence.Identity.Should().BeNull();
    }

    // Aggregation + unique registry
    [Fact]
    public async Task AnalyzeProject_BuildsUniqueSymbolRegistry()
    {
        var project = CreateProject("TestLib",
            ("Calculator.cs", LibrarySource), ("Caller.cs", CallerSource));

        var report = await _service.AnalyzeProjectAsync(project);

        report.TotalDocuments.Should().Be(2);
        report.TotalTypes.Should().Be(2);            // Calculator + Caller
        report.TotalDeclaredMethods.Should().Be(2);  // Add + Run
        report.TotalDeclaredProperties.Should().Be(1);
        report.TotalInvocations.Should().Be(1);      // calc.Add(2,3)
        report.TotalUnresolved.Should().Be(0);

        // Add appears twice in raw evidence (decl + call site) but once in registry
        report.UniqueSymbolKeys.Should().HaveCount(5);
        report.UniqueSymbolKeys
            .Count(k => k.Contains("Lib.Calculator.Add(int,int)")).Should().Be(1);
    }

    public void Dispose() => _workspace.Dispose();
}