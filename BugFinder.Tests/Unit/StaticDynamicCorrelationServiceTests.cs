using System.Linq;
using System.Threading.Tasks;
using ISCM.BugFinder.Core.Models;
using ISCM.BugFinder.Core.Services;
using FluentAssertions;
using Xunit;

namespace ISCM.Tests.Unit.BugFinder;

public class StaticDynamicCorrelationServiceTests : IDisposable
{
    private readonly StaticDynamicCorrelationService _service = new();
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

    public class ChildService
    {
        public int Compute()
        {
            return 42;
        }
    }

    public class Store
    {
        public void Save()
        {
        }
    }
}";

    private async Task<(Microsoft.CodeAnalysis.Project Project, System.Collections.Generic.List<StaticMethodIndexEntry> Index)>
        BuildAsync()
    {
        var ws = _workspace.CreateAdhocWorkspace();
        var project = _workspace.AddInMemoryCSharpProject(ws, "RelProj", ("Rel.cs", Source));
        var index = await _service.BuildMethodIndexAsync(project);
        return (project, index);
    }

    // Static index
    [Fact]
    public async Task BuildMethodIndex_IndexesDeclaredMethodsWithStableKeysAndSpans()
    {
        var (_, index) = await BuildAsync();

        index.Should().HaveCount(4); // GetPower, Log, Compute, Save — Id is a property, not a method

        var compute = index.First(e => e.MethodName == "Compute");
        compute.SymbolKey.Should().Contain("Rel.ChildService.Compute()");
        compute.ContainingTypeName.Should().Be("ChildService");
        compute.ContainingNamespace.Should().Be("Rel");
        compute.FilePath.Should().Be("Rel.cs");
        compute.StartLine.Should().BeGreaterThan(0);
        compute.EndLine.Should().BeGreaterOrEqualTo(compute.StartLine);
    }

    // Stage 2 — stack frame (method name) → symbol
    [Fact]
    public async Task Correlate_StackFrameByNameAndFile_StrongMatch()
    {
        var (_, index) = await BuildAsync();

        var evidence = new DynamicLocationEvidence
        {
            SourceType = DynamicEvidenceSource.Stack,
            FilePath = "Rel.cs",
            MethodName = "Rel.ChildService.Compute",
            SignalStrength = 1.0
        };

        var result = _service.Correlate(evidence, index);

        result.IsCorrelated.Should().BeTrue();
        result.Strategy.Should().Be(CorrelationMatchStrategy.MethodNameAndFile);
        result.MatchConfidence.Should().Be(1.0);
        result.PrimarySymbolKey.Should().Contain("Rel.ChildService.Compute()");
    }

    // Stage 3 — coverage line → containing method
    [Fact]
    public async Task Correlate_CoverageLineWithinMethod_MapsToSymbol()
    {
        var (_, index) = await BuildAsync();
        var compute = index.First(e => e.MethodName == "Compute");

        var evidence = new DynamicLocationEvidence
        {
            SourceType = DynamicEvidenceSource.Coverage,
            FilePath = "Rel.cs",
            LineNumber = compute.StartLine,
            SignalStrength = 0.8
        };

        var result = _service.Correlate(evidence, index);

        result.IsCorrelated.Should().BeTrue();
        result.Strategy.Should().Be(CorrelationMatchStrategy.LineWithinMethod);
        result.PrimarySymbolKey.Should().Contain("Rel.ChildService.Compute()");
    }

    // Weak match — file only
    [Fact]
    public async Task Correlate_FileOnly_WeakMatchListsAllFileMethods()
    {
        var (_, index) = await BuildAsync();

        var evidence = new DynamicLocationEvidence
        {
            SourceType = DynamicEvidenceSource.Runtime,
            FilePath = "Rel.cs"
        };

        var result = _service.Correlate(evidence, index);

        result.IsCorrelated.Should().BeTrue();
        result.Strategy.Should().Be(CorrelationMatchStrategy.FileOnly);
        result.MatchConfidence.Should().Be(0.4);
        result.MatchedMethods.Should().HaveCount(4);
    }

    // Core principle — no match = UNKNOWN, never failure
    [Fact]
    public async Task Correlate_NoMatch_RecordedAsUnknownNotFailure()
    {
        var (_, index) = await BuildAsync();

        var evidence = new DynamicLocationEvidence
        {
            SourceType = DynamicEvidenceSource.Stack,
            FilePath = "Missing.cs",
            MethodName = "Ghost.Method"
        };

        var result = _service.Correlate(evidence, index);

        result.IsCorrelated.Should().BeFalse();
        result.Strategy.Should().Be(CorrelationMatchStrategy.None);
        result.MatchConfidence.Should().Be(0.0);
        result.PrimarySymbolKey.Should().BeNull();
    }

    // BF-12 → BF-13 handshake
    [Fact]
    public async Task CorrelateRankedResult_BridgesBF12CandidatesToStaticSymbols()
    {
        var (project, index) = await BuildAsync();
        var compute = index.First(e => e.MethodName == "Compute");

        var ranked = new RankedLocalizationResult
        {
            Candidates = new System.Collections.Generic.List<EvidenceRankedCandidate>
            {
                new EvidenceRankedCandidate
                {
                    ElementId = "Cand-1", FilePath = "Rel.cs",
                    LineNumber = compute.StartLine,
                    UnifiedScore = 0.9, Rank = 1, ConfidenceLevel = "High"
                },
                new EvidenceRankedCandidate
                {
                    ElementId = "Cand-2", FilePath = "Missing.cs",
                    LineNumber = 10, UnifiedScore = 0.2, Rank = 2, ConfidenceLevel = "Low"
                }
            },
            TotalCandidates = 2
        };

        var report = await _service.CorrelateRankedResultAsync(ranked, project);

        report.TotalDynamicEvidence.Should().Be(2);
        report.TotalCorrelated.Should().Be(1);
        report.TotalUncorrelated.Should().Be(1);
        report.CorrelationRate.Should().BeApproximately(0.5, 0.001);
        report.StaticIndexSize.Should().Be(4);

        report.SymbolKeysWithDynamicSupport.Should().ContainSingle();
        report.SymbolKeysWithDynamicSupport[0].Should().Contain("Rel.ChildService.Compute()");
    }

    public void Dispose() => _workspace.Dispose();
}