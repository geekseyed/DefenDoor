using System;
using System.Linq;
using System.Threading.Tasks;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class SyntaxAnalysisServiceTests : IDisposable
{
    private readonly SyntaxAnalysisService _service = new();
    private readonly RoslynWorkspaceService _workspace = new();

    private const string SampleSource = @"
using System;

namespace Sample
{
    public class Calculator
    {
        public int Add(int a, int b)
        {
            var sum = a + b;
            return sum;
        }

        public int Square(int x) => x * x;
    }
}";

    // Stage 1
    [Fact]
    public void ParseSource_CreatesTree_WithFilePath()
    {
        var tree = _service.ParseSource(SampleSource, "Calculator.cs");

        tree.Should().NotBeNull();
        tree.FilePath.Should().Be("Calculator.cs");
    }

    // Stage 2
    [Fact]
    public void AnalyzeTree_FindsMethods_WithLinesAndStatements()
    {
        var tree = _service.ParseSource(SampleSource, "Calculator.cs");
        var evidence = _service.AnalyzeTree(tree);

        evidence.MethodCount.Should().Be(2);
        evidence.TypeCount.Should().Be(1);

        var add = evidence.Methods.First(m => m.MethodName == "Add");
        add.ContainingTypeName.Should().Be("Calculator");
        add.StartLine.Should().BeGreaterThan(0);
        add.EndLine.Should().BeGreaterOrEqualTo(add.StartLine);
        add.StatementCount.Should().Be(2);
        add.ParameterCount.Should().Be(2);
        add.HasExecutableBody.Should().BeTrue();
        add.IsExpressionBodied.Should().BeFalse();
    }

    // Stage 3 — expression-bodied member
    [Fact]
    public void AnalyzeTree_ExpressionBodiedMethod_Detected()
    {
        var tree = _service.ParseSource(SampleSource, "Calculator.cs");
        var evidence = _service.AnalyzeTree(tree);

        var square = evidence.Methods.First(m => m.MethodName == "Square");
        square.IsExpressionBodied.Should().BeTrue();
        square.StatementCount.Should().Be(0);
    }

    // Stage 4 — aggregation
    [Fact]
    public void Analyze_AggregatesMultipleTrees()
    {
        var t1 = _service.ParseSource(SampleSource, "Calculator.cs");
        var t2 = _service.ParseSource(@"
namespace Other
{
    public class Logger
    {
        public void Log(string message)
        {
        }
    }
}", "Logger.cs");

        var report = _service.Analyze(new[] { t1, t2 });

        report.TotalTrees.Should().Be(2);
        report.TotalMethods.Should().Be(3);
        report.TotalTypes.Should().Be(2);
    }

    // Stage 4 — integration with 13.1 workspace
    [Fact]
    public async Task AnalyzeProjectAsync_AnalyzesAllDocuments()
    {
        var ws = _workspace.CreateAdhocWorkspace();
        var project = _workspace.AddInMemoryCSharpProject(ws, "Proj",
            ("Calculator.cs", SampleSource),
            ("Logger.cs", "namespace Other { public class Logger { public void Log(string m) { } } }"));

        var report = await _service.AnalyzeProjectAsync(project);

        report.TotalTrees.Should().Be(2);
        report.TotalMethods.Should().Be(3);
    }

    public void Dispose() => _workspace.Dispose();
}