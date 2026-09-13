using FluentAssertions;
using ISCM.Application.Interfaces;
using ISCM.Application.Services;
using ISCM.Application.Services.Agreement;
using ISCM.Domain.Entities;
using ISCM.Domain.Enums;
using ISCM.Domain.ValueObjects;
using ISCM.Infrastructure.Scanning;
using ISCM.Infrastructure.Scanning.Collectors;
using Moq;
using System.Threading;
using Xunit;

namespace ISCM.Tests.Integration.Scanner;

/// <summary>
/// Phase 14.5: Portable integration tests for parallel scan execution.
///
/// Uses concurrency tracking instead of timing assertions.
/// IControlEvaluator is unconfigured mock - findings may be null (test artifact).
/// </summary>
public class ParallelScanIntegrationTests
{
    private sealed class ConcurrencyTracker
    {
        private int _current = 0;
        private int _maxConcurrent = 0;
        private int _totalEntries = 0;

        public void Enter()
        {
            Interlocked.Increment(ref _totalEntries);
            var current = Interlocked.Increment(ref _current);

            int observed;
            do
            {
                observed = Volatile.Read(ref _maxConcurrent);
                if (current <= observed) break;
            }
            while (Interlocked.CompareExchange(ref _maxConcurrent, current, observed) != observed);
        }

        public void Exit()
        {
            Interlocked.Decrement(ref _current);
        }

        public int MaxConcurrent => Volatile.Read(ref _maxConcurrent);
        public int TotalEntries => Volatile.Read(ref _totalEntries);
    }

    private sealed class ConcurrencyTrackingCheck : IHardeningCheck, IEvidenceCollector
    {
        public string CheckId { get; }
        public string Name => CheckId;
        public CheckCategory Category => CheckCategory.System;
        public CheckSeverity Severity => CheckSeverity.Low;
        public string CollectorId => CheckId;

        private readonly ConcurrencyTracker _tracker;
        private readonly int _delayMs;

        public ConcurrencyTrackingCheck(string checkId, ConcurrencyTracker tracker, int delayMs)
        {
            CheckId = checkId;
            _tracker = tracker;
            _delayMs = delayMs;
        }

        public async Task<List<Evidence>> CollectEvidenceAsync()
        {
            _tracker.Enter();
            try
            {
                await Task.Delay(_delayMs);
                return new List<Evidence>
                {
                    new Evidence
                    {
                        EvidenceId = Guid.NewGuid().ToString(),
                        SubControlId = $"{CheckId}.1",
                        TechnicalCheckId = CheckId,
                        SourceType = EvidenceSourceType.Other,
                        SourceName = "ConcurrencyTracker",
                        TypedValue = EvidenceValue.FromBoolean(true),
                        Evaluation = CheckStatus.NotScanned
                    }
                };
            }
            finally
            {
                _tracker.Exit();
            }
        }
    }

    private static WindowsHardeningScanner BuildScanner(
        List<IHardeningCheck> checks,
        int maxDegreeOfParallelism)
    {
        var systemInfoCollector = new WindowsSystemInfoCollector();

        var mockBaseline = new Mock<IBaselineService>();
        mockBaseline.Setup(x => x.GetDefaultBaseline()).Returns(new BaselineDefinition
        {
            BaselineId = "test",
            Name = "Test",
            Version = "1.0"
        });

        var mockConfig = new Mock<IScannerConfigurationService>();
        mockConfig.Setup(x => x.GetMaxDegreeOfParallelism()).Returns(maxDegreeOfParallelism);

        return new WindowsHardeningScanner(
            systemInfoCollector,
            checks,
            Mock.Of<IControlEvaluator>(),
            mockBaseline.Object,
            Mock.Of<IEvidenceAcquisitionService>(),
            Mock.Of<IScanFreshnessPolicy>(),
            Mock.Of<IFingerprintValidationService>(),
            Mock.Of<IScanInvalidationService>(),
            Mock.Of<INormalizationService>(),
            Mock.Of<VerificationPathService>(),
            new SubControlAggregationService(Mock.Of<IAgreementPolicy>()),
            mockConfig.Object);
    }

    // =========================================================================
    // Test 1: Completeness - all 3 collectors executed
    // =========================================================================

    [Fact]
    public async Task RunScanAsync_ExecutesAllChecks_ReturnsCompleteResult()
    {
        // Arrange
        var tracker = new ConcurrencyTracker();
        var checks = new List<IHardeningCheck>
        {
            new ConcurrencyTrackingCheck("T-001", tracker, 100),
            new ConcurrencyTrackingCheck("T-002", tracker, 100),
            new ConcurrencyTrackingCheck("T-003", tracker, 100)
        };

        var scanner = BuildScanner(checks, maxDegreeOfParallelism: 3);

        // Act
        var result = await scanner.RunScanAsync();

        // Assert
        result.Should().NotBeNull();
        result.ScanId.Should().NotBeNullOrEmpty();

        // All 3 collectors ran to completion
        tracker.TotalEntries.Should().Be(3, "all 3 checks should have executed");

        // ScanResult has 3 finding slots (even if null due to unconfigured evaluator mock)
        result.Findings.Should().HaveCount(3);

        // No collector crashed (crash findings are NON-NULL with Error status)
        result.Findings.Should().NotContain(
            f => f != null && f.Status == CheckStatus.Error);
    }

    // =========================================================================
    // Test 2: Parallel Execution - MaxDegreeOfParallelism=3
    // =========================================================================

    [Fact]
    public async Task RunScanAsync_WithParallelism3_ExecutesChecksConcurrently()
    {
        // Arrange
        var tracker = new ConcurrencyTracker();
        var checks = new List<IHardeningCheck>
        {
            new ConcurrencyTrackingCheck("PAR-001", tracker, 300),
            new ConcurrencyTrackingCheck("PAR-002", tracker, 300),
            new ConcurrencyTrackingCheck("PAR-003", tracker, 300)
        };

        var scanner = BuildScanner(checks, maxDegreeOfParallelism: 3);

        // Act
        await scanner.RunScanAsync();

        // Assert
        tracker.MaxConcurrent.Should().Be(3,
            "3 checks should execute concurrently when MaxDegreeOfParallelism=3");
    }

    // =========================================================================
    // Test 3: Sequential Execution - MaxDegreeOfParallelism=1
    // =========================================================================

    [Fact]
    public async Task RunScanAsync_WithParallelism1_ExecutesChecksSequentially()
    {
        // Arrange
        var tracker = new ConcurrencyTracker();
        var checks = new List<IHardeningCheck>
        {
            new ConcurrencyTrackingCheck("SEQ-001", tracker, 100),
            new ConcurrencyTrackingCheck("SEQ-002", tracker, 100),
            new ConcurrencyTrackingCheck("SEQ-003", tracker, 100)
        };

        var scanner = BuildScanner(checks, maxDegreeOfParallelism: 1);

        // Act
        await scanner.RunScanAsync();

        // Assert
        tracker.MaxConcurrent.Should().Be(1,
            "only 1 check should execute at a time when MaxDegreeOfParallelism=1");
    }
}