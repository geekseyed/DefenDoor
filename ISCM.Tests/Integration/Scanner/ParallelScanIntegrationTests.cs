using FluentAssertions;
using ISCM.Application.Interfaces;
using ISCM.Application.Services;
using ISCM.Application.Services.Agreement;
using ISCM.Domain.Entities;
using ISCM.Domain.Enums;
using ISCM.Infrastructure.Scanning;
using ISCM.Infrastructure.Scanning.Collectors;
using Moq;
using System.Diagnostics;
using Xunit;

namespace ISCM.Tests.Integration.Scanner;

public class ParallelScanIntegrationTests
{
    [Fact]
    public async Task RunScanAsync_ExecutesChecksInParallel_CompletesFaster()
    {
        // Arrange: Use real WindowsSystemInfoCollector (fast registry reads)
        // Moq cannot mock non-virtual methods, so we use the real collector.
        var systemInfoCollector = new WindowsSystemInfoCollector();

        var mockBaseline = new Mock<IBaselineService>();
        mockBaseline.Setup(x => x.GetDefaultBaseline()).Returns(new BaselineDefinition
        {
            BaselineId = "test",
            Name = "Test",
            Version = "1.0"
        });

        // ═══════════════════════════════════════════════════════════════
        // FIX: Use explicit concurrency = 3 instead of Environment.ProcessorCount
        // This ensures the test is deterministic regardless of CPU count.
        // 3 checks with 500ms each should complete in ~500-800ms when parallel.
        // ═══════════════════════════════════════════════════════════════
        var mockConfig = new Mock<IScannerConfigurationService>();
        mockConfig.Setup(x => x.GetMaxDegreeOfParallelism()).Returns(3);

        // Create 3 slow checks (500ms each)
        var checks = new List<IHardeningCheck>
        {
            new SlowMockCheck("SLOW-001", 500),
            new SlowMockCheck("SLOW-002", 500),
            new SlowMockCheck("SLOW-003", 500)
        };

        var scanner = new WindowsHardeningScanner(
            systemInfoCollector,  // ← REAL collector, not mock
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

        // Act
        var sw = Stopwatch.StartNew();
        var result = await scanner.RunScanAsync();
        sw.Stop();

        // Assert
        result.Findings.Should().HaveCount(3);
        sw.ElapsedMilliseconds.Should().BeLessThan(1200,
            "3x500ms checks should run concurrently in ~500-800ms, not serially");
    }

    private class SlowMockCheck : IHardeningCheck, IEvidenceCollector
    {
        public string CheckId { get; }
        public string Name => CheckId;
        public CheckCategory Category => CheckCategory.System;
        public CheckSeverity Severity => CheckSeverity.Low;
        private readonly int _delayMs;

        public SlowMockCheck(string id, int delayMs)
        {
            CheckId = id;
            _delayMs = delayMs;
        }

        public string CollectorId => CheckId;

        public async Task<List<Evidence>> CollectEvidenceAsync()
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
                    SourceName = "Mock",
                    TypedValue = Domain.ValueObjects.EvidenceValue.FromBoolean(true),
                    Evaluation = CheckStatus.NotScanned
                }
            };
        }
    }
}