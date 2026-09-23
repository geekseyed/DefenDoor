using System;
using System.Linq;
using System.Threading.Tasks;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class SemanticModelServiceTests : IDisposable
{
    private readonly SemanticModelService _service = new();
    private readonly RoslynWorkspaceService _workspace = new();

    private const string CalculatorSource = @"
using System;

namespace Sample
{
    public class Calculator
    {
        public int Add(int a, int b)
        {
            return Math.Max(a, b);
        }

        public string Describe() => ""calc"";
    }
}";

    private const string BrokenSource = @"
namespace Broken
{
    public class Caller
    {
        public void Call()
        {
            UndefinedHelper.Foo();
        }
    }
}";

    // Stage 3 — declared symbols
    [Fact]
    public async Task AnalyzeDocument_ResolvesDeclaredMethods_WithTypes()
    {
        var doc = CreateProject("Calc", ("Calculator.cs", CalculatorSource)).Documents.First();

        var evidence = await _service.AnalyzeDocumentAsync(doc);

        var add = evidence.DeclaredMethods.First(m => m.MethodName == "Add");
        add.ReturnType.Should().Be("int");
        add.ParameterCount.Should().Be(2);
        add.ParameterTypes.Should().Contain("int");
        add.IsStatic.Should().BeFalse();
        add.FullSignature.Should().Contain("Add");

        var describe = evidence.DeclaredMethods.First(m => m.MethodName == "Describe");
        describe.ReturnType.Should().Be("string");
    }

    // Stage 3 — invoked symbol resolution (static method across assembly)
    [Fact]
    public async Task AnalyzeDocument_ResolvesInvokedSymbol_WithContainingTypeAndNamespace()
    {
        var doc = CreateProject("Calc", ("Calculator.cs", CalculatorSource)).Documents.First();

        var evidence = await _service.AnalyzeDocumentAsync(doc);

        var max = evidence.InvokedSymbols.First(s => s.IsResolved);
        max.Kind.Should().Be(SemanticSymbolKind.Method);
        max.ContainingTypeName.Should().Be("Math");
        max.ContainingNamespace.Should().Be("System");
        max.IsStatic.Should().BeTrue();
        max.DisplayName.Should().Contain("Max");
    }

    // Stage 2 — expression type info
    [Fact]
    public async Task AnalyzeDocument_ResolvesExpressionType_OfInvocation()
    {
        var doc = CreateProject("Calc", ("Calculator.cs", CalculatorSource)).Documents.First();

        var evidence = await _service.AnalyzeDocumentAsync(doc);

        var intExpr = evidence.ExpressionTypes.First(t => t.Expression.Contains("Math.Max"));
        intExpr.IsResolved.Should().BeTrue();
        intExpr.FullTypeName.Should().Be("int");
    }

    // Unknown symbol → UNKNOWN, not failure (core principle)
    [Fact]
    public async Task AnalyzeDocument_UnresolvedInvocation_RecordedAsUnknown()
    {
        var doc = CreateProject("Broken", ("Caller.cs", BrokenSource)).Documents.First();

        var evidence = await _service.AnalyzeDocumentAsync(doc);

        var unresolved = evidence.InvokedSymbols.Where(s => !s.IsResolved).ToList();
        unresolved.Should().HaveCount(1);
        unresolved[0].Kind.Should().Be(SemanticSymbolKind.Unknown);
        evidence.UnresolvedCount.Should().Be(1);
    }

    // Stage 4 — project aggregation
    [Fact]
    public async Task AnalyzeProject_AggregatesMultipleDocuments()
    {
        var project = CreateProject("Multi",
            ("Calculator.cs", CalculatorSource),
            ("Caller.cs", BrokenSource));

        var report = await _service.AnalyzeProjectAsync(project);

        report.TotalDocuments.Should().Be(2);
        report.TotalMethods.Should().Be(3); // Add, Describe, Call
        report.TotalResolvedSymbols.Should().Be(1); // Math.Max
        report.TotalUnresolvedSymbols.Should().Be(1); // UndefinedHelper.Foo
    }

    private Microsoft.CodeAnalysis.Project CreateProject(
        string name, params (string Name, string Source)[] files)
    {
        var ws = _workspace.CreateAdhocWorkspace();
        return _workspace.AddInMemoryCSharpProject(ws, name, files);
    }

    public void Dispose() => _workspace.Dispose();
}