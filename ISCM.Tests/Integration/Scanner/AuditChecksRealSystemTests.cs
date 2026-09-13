using ISCM.Application.Evaluators;
using ISCM.Application.Evaluators.Typed;
using ISCM.Application.Interfaces;
using ISCM.Application.Services;
using ISCM.Domain.Entities;
using ISCM.Domain.Enums;
using ISCM.Infrastructure.Scanning;
using ISCM.Infrastructure.Scanning.Checks;
using Xunit;
using FluentAssertions;

namespace ISCM.Tests.Integration.Scanner;

/// <summary>
/// Real-system integration tests for Phase 10.7 Audit Checks migration.
/// Phase 14.3: Updated to inject IProcessCacheService into checks that need it.
///
/// 4 checks migrated:
///   - AdvancedAuditCheck (AUD-001, 11 SubControls)
///   - ProcessCreationAuditingCheck (PRC-001, 3 SubControls)
///   - PowerShellLoggingCheck (PSH-001, 3 SubControls)
///   - EventLogSizeCheck (EVL-001, 6 SubControls)
/// </summary>
public class AuditChecksRealSystemTests
{
    /// <summary>
    /// Phase 14.3: Helper to create real process cache for tests.
    /// </summary>
    private static IProcessCacheService CreateProcessCache()
    {
        var processRunner = new ProcessRunner();
        var configService = new ScannerConfigurationService();
        return new CachedProcessRunner(processRunner, configService);
    }

    private static (ExpectedValueParser, TypedEvidenceEvaluator, ControlEvaluator) BuildTypedPipeline()
    {
        var parser = new ExpectedValueParser();
        var typedEvaluator = new TypedEvidenceEvaluator(
            parser,
            new IntegerEvaluator(), new LongEvaluator(), new BooleanEvaluator(),
            new StringEvaluator(), new DurationEvaluator(), new SizeEvaluator(),
            new EnumEvaluator(), new CollectionEvaluator(), new RegistryValueEvaluator(),
            new PolicyValueEvaluator());

        var controlEvaluator = new ControlEvaluator(typedEvaluator);
        return (parser, typedEvaluator, controlEvaluator);
    }

    // =========================================================================
    // AdvancedAuditCheck (AUD-001) - 11 SubControls
    // =========================================================================

    [Fact]
    public async Task AdvancedAuditCheck_RealSystem_ProducesValidFindings()
    {
        var (_, _, controlEvaluator) = BuildTypedPipeline();
        var processCache = CreateProcessCache();
        var check = new AdvancedAuditCheck(processCache);

        var evidenceList = await check.CollectEvidenceAsync();

        evidenceList.Should().NotBeNull();
        evidenceList.Should().HaveCount(11, "AdvancedAuditCheck has 11 SubControls");

        Console.WriteLine("\n=== AdvancedAuditCheck - Summary ===");
        foreach (var evidence in evidenceList)
        {
            Console.WriteLine($"  {evidence.SubControlId}: TypedValue={evidence.TypedValue}");
        }

        var subControlResults = evidenceList
            .GroupBy(e => e.SubControlId)
            .Select(g =>
            {
                var subControlId = g.Key;
                var controlDefinition = ControlCatalog.GetByCheckId("AUD-001");
                var subControlDef = controlDefinition?.SubControls
                    .FirstOrDefault(s => s.SubControlId == subControlId);

                var subResult = new SubControlResult
                {
                    SubControlId = subControlId,
                    Status = CheckStatus.NotScanned,
                    EvidenceItems = g.ToList(),
                    EvaluatedAt = DateTime.UtcNow
                };

                if (subControlDef != null && !string.IsNullOrWhiteSpace(subControlDef.ExpectedValue))
                {
                    controlEvaluator.EvaluateSubControlTyped(
                        subResult,
                        subControlDef.ExpectedValue,
                        subControlDef.ExpectedValueType,
                        subControlDef.Operator
                    );
                }

                return subResult;
            })
            .ToList();

        subControlResults.Should().HaveCount(11);

        Console.WriteLine("\n=== AdvancedAuditCheck - Evaluation ===");
        foreach (var r in subControlResults)
        {
            var ev = r.EvidenceItems.First();
            Console.WriteLine($"  {r.SubControlId}: {r.Status} (TypedValue={ev.TypedValue}, Reason={ev.EvaluationReason})");
        }

        foreach (var subResult in subControlResults)
        {
            subResult.Status.Should().NotBe(CheckStatus.Error,
                $"SubControl {subResult.SubControlId} should not have Error status");
        }
    }

    [Fact]
    public async Task AdvancedAuditCheck_CollectEvidenceAsync_ReturnsCorrectSubControlIds()
    {
        var processCache = CreateProcessCache();
        var check = new AdvancedAuditCheck(processCache);
        var evidenceList = await check.CollectEvidenceAsync();
        var subControlIds = evidenceList.Select(e => e.SubControlId).ToList();

        subControlIds.Should().HaveCount(11);
        subControlIds.Should().Contain("AUD-001.1", "Logon");
        subControlIds.Should().Contain("AUD-001.11", "Security System Extension");
    }

    // =========================================================================
    // ProcessCreationAuditingCheck (PRC-001) - 3 SubControls
    // =========================================================================

    [Fact]
    public async Task ProcessCreationAuditingCheck_RealSystem_ProducesValidFindings()
    {
        var (_, _, controlEvaluator) = BuildTypedPipeline();
        var check = new ProcessCreationAuditingCheck();

        var evidenceList = await check.CollectEvidenceAsync();

        evidenceList.Should().NotBeNull();
        evidenceList.Should().HaveCount(3, "PRC-001 has 3 SubControls");

        Console.WriteLine("\n=== ProcessCreationAuditingCheck - Evidence ===");
        foreach (var evidence in evidenceList)
        {
            Console.WriteLine($"  {evidence.SubControlId}: TypedValue={evidence.TypedValue}");
        }

        var subControlResults = evidenceList
            .GroupBy(e => e.SubControlId)
            .Select(g =>
            {
                var subControlId = g.Key;
                var controlDefinition = ControlCatalog.GetByCheckId("PRC-001");
                var subControlDef = controlDefinition?.SubControls
                    .FirstOrDefault(s => s.SubControlId == subControlId);

                var subResult = new SubControlResult
                {
                    SubControlId = subControlId,
                    Status = CheckStatus.NotScanned,
                    EvidenceItems = g.ToList(),
                    EvaluatedAt = DateTime.UtcNow
                };

                if (subControlDef != null && !string.IsNullOrWhiteSpace(subControlDef.ExpectedValue))
                {
                    controlEvaluator.EvaluateSubControlTyped(
                        subResult,
                        subControlDef.ExpectedValue,
                        subControlDef.ExpectedValueType,
                        subControlDef.Operator
                    );
                }

                return subResult;
            })
            .ToList();

        subControlResults.Should().HaveCount(3);

        Console.WriteLine("\n=== ProcessCreationAuditingCheck - Evaluation ===");
        foreach (var r in subControlResults)
        {
            var ev = r.EvidenceItems.First();
            Console.WriteLine($"  {r.SubControlId}: {r.Status} (Reason={ev.EvaluationReason})");
        }

        foreach (var subResult in subControlResults)
        {
            subResult.Status.Should().NotBe(CheckStatus.Error,
                $"SubControl {subResult.SubControlId} should not have Error status");
        }
    }

    // =========================================================================
    // PowerShellLoggingCheck (PSH-001) - 3 SubControls
    // =========================================================================

    [Fact]
    public async Task PowerShellLoggingCheck_RealSystem_ProducesValidFindings()
    {
        var (_, _, controlEvaluator) = BuildTypedPipeline();
        var check = new PowerShellLoggingCheck();

        var evidenceList = await check.CollectEvidenceAsync();

        evidenceList.Should().NotBeNull();
        evidenceList.Should().HaveCount(3, "PSH-001 has 3 SubControls");

        Console.WriteLine("\n=== PowerShellLoggingCheck - Evidence ===");
        foreach (var evidence in evidenceList)
        {
            Console.WriteLine($"  {evidence.SubControlId}: TypedValue={evidence.TypedValue}");
        }

        var subControlResults = evidenceList
            .GroupBy(e => e.SubControlId)
            .Select(g =>
            {
                var subControlId = g.Key;
                var controlDefinition = ControlCatalog.GetByCheckId("PSH-001");
                var subControlDef = controlDefinition?.SubControls
                    .FirstOrDefault(s => s.SubControlId == subControlId);

                var subResult = new SubControlResult
                {
                    SubControlId = subControlId,
                    Status = CheckStatus.NotScanned,
                    EvidenceItems = g.ToList(),
                    EvaluatedAt = DateTime.UtcNow
                };

                if (subControlDef != null && !string.IsNullOrWhiteSpace(subControlDef.ExpectedValue))
                {
                    controlEvaluator.EvaluateSubControlTyped(
                        subResult,
                        subControlDef.ExpectedValue,
                        subControlDef.ExpectedValueType,
                        subControlDef.Operator
                    );
                }

                return subResult;
            })
            .ToList();

        subControlResults.Should().HaveCount(3);

        Console.WriteLine("\n=== PowerShellLoggingCheck - Evaluation ===");
        foreach (var r in subControlResults)
        {
            var ev = r.EvidenceItems.First();
            Console.WriteLine($"  {r.SubControlId}: {r.Status} (Reason={ev.EvaluationReason})");
        }

        foreach (var subResult in subControlResults)
        {
            subResult.Status.Should().NotBe(CheckStatus.Error,
                $"SubControl {subResult.SubControlId} should not have Error status");
        }
    }

    // =========================================================================
    // EventLogSizeCheck (EVL-001) - 6 SubControls
    // =========================================================================

    [Fact]
    public async Task EventLogSizeCheck_RealSystem_ProducesValidFindings()
    {
        var (_, _, controlEvaluator) = BuildTypedPipeline();
        var check = new EventLogSizeCheck();

        var evidenceList = await check.CollectEvidenceAsync();

        evidenceList.Should().NotBeNull();
        evidenceList.Should().HaveCount(6, "EVL-001 migrated 6 SubControls");

        Console.WriteLine("\n=== EventLogSizeCheck - Evidence ===");
        foreach (var evidence in evidenceList)
        {
            Console.WriteLine($"  {evidence.SubControlId}: TypedValue={evidence.TypedValue}");
        }

        var subControlResults = evidenceList
            .GroupBy(e => e.SubControlId)
            .Select(g =>
            {
                var subControlId = g.Key;
                var controlDefinition = ControlCatalog.GetByCheckId("EVL-001");
                var subControlDef = controlDefinition?.SubControls
                    .FirstOrDefault(s => s.SubControlId == subControlId);

                var subResult = new SubControlResult
                {
                    SubControlId = subControlId,
                    Status = CheckStatus.NotScanned,
                    EvidenceItems = g.ToList(),
                    EvaluatedAt = DateTime.UtcNow
                };

                if (subControlDef != null && !string.IsNullOrWhiteSpace(subControlDef.ExpectedValue))
                {
                    controlEvaluator.EvaluateSubControlTyped(
                        subResult,
                        subControlDef.ExpectedValue,
                        subControlDef.ExpectedValueType,
                        subControlDef.Operator
                    );
                }

                return subResult;
            })
            .ToList();

        subControlResults.Should().HaveCount(6);

        Console.WriteLine("\n=== EventLogSizeCheck - Evaluation ===");
        foreach (var r in subControlResults)
        {
            var ev = r.EvidenceItems.First();
            Console.WriteLine($"  {r.SubControlId}: {r.Status} (Reason={ev.EvaluationReason})");
        }

        foreach (var subResult in subControlResults)
        {
            subResult.Status.Should().NotBe(CheckStatus.Error,
                $"SubControl {subResult.SubControlId} should not have Error status");
        }
    }
}