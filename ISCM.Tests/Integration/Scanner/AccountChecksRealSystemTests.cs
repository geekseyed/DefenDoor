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
/// Real-system integration tests for Phase 10.6 Account Checks migration.
/// Phase 14.3: Updated to inject IProcessCacheService into checks.
/// </summary>
public class AccountChecksRealSystemTests
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

    // =========================================================================
    // AccountLockoutCheck (LCK-001) Tests
    // =========================================================================

    [Fact]
    public async Task AccountLockoutCheck_RealSystem_ProducesValidFindings()
    {
        // Arrange
        var parser = new ExpectedValueParser();
        var typedEvaluator = new TypedEvidenceEvaluator(
            parser,
            new IntegerEvaluator(), new LongEvaluator(), new BooleanEvaluator(),
            new StringEvaluator(), new DurationEvaluator(), new SizeEvaluator(),
            new EnumEvaluator(), new CollectionEvaluator(), new RegistryValueEvaluator(),
            new PolicyValueEvaluator());

        var controlEvaluator = new ControlEvaluator(typedEvaluator);
        var processCache = CreateProcessCache();
        var lockoutCheck = new AccountLockoutCheck(processCache);

        // Act - Collect evidence
        var evidenceList = await lockoutCheck.CollectEvidenceAsync();

        // Assert - Collection
        evidenceList.Should().NotBeNull();
        evidenceList.Should().HaveCount(3, "AccountLockoutCheck has 3 SubControls");

        Console.WriteLine("\n=== AccountLockoutCheck - Collected Evidence ===");
        foreach (var evidence in evidenceList)
        {
            Console.WriteLine($"SubControlId: {evidence.SubControlId}");
            Console.WriteLine($"  RawOutput (first 100 chars): {evidence.RawOutput?.Substring(0, Math.Min(100, evidence.RawOutput?.Length ?? 0))}");
            Console.WriteLine($"  TypedValue: {evidence.TypedValue}");
            Console.WriteLine($"  Evaluation: {evidence.Evaluation}");
        }

        // Act - Group + evaluate
        var subControlResults = evidenceList
            .GroupBy(e => e.SubControlId)
            .Select(g =>
            {
                var subControlId = g.Key;
                var controlDefinition = ControlCatalog.GetByCheckId("LCK-001");
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

        // Assert - Evaluation
        subControlResults.Should().HaveCount(3);

        Console.WriteLine("\n=== AccountLockoutCheck - Evaluation Results ===");
        foreach (var subResult in subControlResults)
        {
            Console.WriteLine($"SubControlId: {subResult.SubControlId}");
            Console.WriteLine($"  Status: {subResult.Status}");
            var evidence = subResult.EvidenceItems.First();
            Console.WriteLine($"  TypedValue: {evidence.TypedValue}");
            Console.WriteLine($"  Evaluation Reason: {evidence.EvaluationReason ?? "N/A"}");
        }

        // All SubControls should have valid status (not Error or NotScanned)
        foreach (var subResult in subControlResults)
        {
            subResult.Status.Should().NotBe(CheckStatus.Error,
                $"SubControl {subResult.SubControlId} should not have Error status");
            subResult.Status.Should().NotBe(CheckStatus.NotScanned,
                $"SubControl {subResult.SubControlId} should have been evaluated");
        }

        // Summary
        Console.WriteLine("\n=== AccountLockoutCheck - Summary ===");
        var passCount = subControlResults.Count(r => r.Status == CheckStatus.Pass);
        var failCount = subControlResults.Count(r => r.Status == CheckStatus.Fail);
        Console.WriteLine($"Pass: {passCount}/3, Fail: {failCount}/3");

        var evaluatedCount = passCount + failCount;
        evaluatedCount.Should().BeGreaterThan(0,
            "at least some SubControls should be successfully evaluated");
    }

    [Fact]
    public async Task AccountLockoutCheck_CollectEvidenceAsync_ReturnsCorrectSubControlIds()
    {
        var processCache = CreateProcessCache();
        var lockoutCheck = new AccountLockoutCheck(processCache);
        var evidenceList = await lockoutCheck.CollectEvidenceAsync();
        var subControlIds = evidenceList.Select(e => e.SubControlId).ToList();

        subControlIds.Should().Contain("LCK-001.1", "Account lockout threshold");
        subControlIds.Should().Contain("LCK-001.2", "Account lockout duration");
        subControlIds.Should().Contain("LCK-001.3", "Reset account lockout counter after");
    }

    // =========================================================================
    // GuestAccountCheck (GUEST-001) Tests
    // =========================================================================

    [Fact]
    public async Task GuestAccountCheck_RealSystem_ProducesValidFindings()
    {
        // Arrange
        var parser = new ExpectedValueParser();
        var typedEvaluator = new TypedEvidenceEvaluator(
            parser,
            new IntegerEvaluator(), new LongEvaluator(), new BooleanEvaluator(),
            new StringEvaluator(), new DurationEvaluator(), new SizeEvaluator(),
            new EnumEvaluator(), new CollectionEvaluator(), new RegistryValueEvaluator(),
            new PolicyValueEvaluator());

        var controlEvaluator = new ControlEvaluator(typedEvaluator);
        var guestCheck = new GuestAccountCheck();

        // Act - Collect evidence
        var evidenceList = await guestCheck.CollectEvidenceAsync();

        // Assert - Collection
        evidenceList.Should().NotBeNull();
        evidenceList.Should().HaveCount(2, "GuestAccountCheck has 2 SubControls");

        Console.WriteLine("\n=== GuestAccountCheck - Collected Evidence ===");
        foreach (var evidence in evidenceList)
        {
            Console.WriteLine($"SubControlId: {evidence.SubControlId}");
            Console.WriteLine($"  RawOutput: {evidence.RawOutput?.Trim()}");
            Console.WriteLine($"  TypedValue: {evidence.TypedValue}");
            Console.WriteLine($"  Evaluation: {evidence.Evaluation}");
        }

        // Act - Group + evaluate
        var subControlResults = evidenceList
            .GroupBy(e => e.SubControlId)
            .Select(g =>
            {
                var subControlId = g.Key;
                var controlDefinition = ControlCatalog.GetByCheckId("GUEST-001");
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

        // Assert - Evaluation
        subControlResults.Should().HaveCount(2);

        Console.WriteLine("\n=== GuestAccountCheck - Evaluation Results ===");
        foreach (var subResult in subControlResults)
        {
            Console.WriteLine($"SubControlId: {subResult.SubControlId}");
            Console.WriteLine($"  Status: {subResult.Status}");
            var evidence = subResult.EvidenceItems.First();
            Console.WriteLine($"  TypedValue: {evidence.TypedValue}");
            Console.WriteLine($"  Evaluation Reason: {evidence.EvaluationReason ?? "N/A"}");
        }

        // All SubControls should have valid status
        foreach (var subResult in subControlResults)
        {
            subResult.Status.Should().NotBe(CheckStatus.Error,
                $"SubControl {subResult.SubControlId} should not have Error status");
            subResult.Status.Should().NotBe(CheckStatus.NotScanned,
                $"SubControl {subResult.SubControlId} should have been evaluated");
        }

        // Summary
        Console.WriteLine("\n=== GuestAccountCheck - Summary ===");
        var passCount = subControlResults.Count(r => r.Status == CheckStatus.Pass);
        var failCount = subControlResults.Count(r => r.Status == CheckStatus.Fail);
        Console.WriteLine($"Pass: {passCount}/2, Fail: {failCount}/2");

        var evaluatedCount = passCount + failCount;
        evaluatedCount.Should().BeGreaterThan(0,
            "at least some SubControls should be successfully evaluated");
    }

    [Fact]
    public async Task GuestAccountCheck_CollectEvidenceAsync_ReturnsCorrectSubControlIds()
    {
        var guestCheck = new GuestAccountCheck();
        var evidenceList = await guestCheck.CollectEvidenceAsync();
        var subControlIds = evidenceList.Select(e => e.SubControlId).ToList();

        subControlIds.Should().Contain("GUEST-001.1", "Guest account status");
        subControlIds.Should().Contain("GUEST-001.2", "Rename guest account");
    }
}