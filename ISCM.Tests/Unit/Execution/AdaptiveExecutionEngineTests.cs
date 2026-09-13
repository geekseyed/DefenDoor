namespace ISCM.Tests.Unit.Execution;

using FluentAssertions;
using ISCM.Application.Interfaces;
using ISCM.Application.Services;
using ISCM.Domain.Entities;
using ISCM.Domain.Enums;
using ISCM.Domain.ValueObjects;
using ISCM.Infrastructure.Scanning.Collectors;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Phase 14.2: Unit tests for AdaptiveExecutionEngine.
/// Uses real EnvironmentDetector and ScannerConfigurationService.
/// </summary>
public class AdaptiveExecutionEngineTests
{
    private readonly IAdaptiveExecutionEngine _engine;
    private readonly IEnvironmentDetector _detector;
    private readonly ScannerConfigurationService _configService;

    public AdaptiveExecutionEngineTests()
    {
        _detector = new EnvironmentDetector();
        _configService = new ScannerConfigurationService();
        _engine = new AdaptiveExecutionEngine(_detector, _configService);
    }

    [Fact]
    public async Task GetExecutionProfileAsync_ReturnsValidProfile()
    {
        // Act
        var profile = await _engine.GetExecutionProfileAsync();

        // Assert
        profile.Should().NotBeNull();
        profile.ComputedAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
        profile.EffectiveMaxDegreeOfParallelism.Should().BeGreaterThan(0);
        profile.Reasoning.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetExecutionProfileAsync_CachesResult()
    {
        // Act
        var profile1 = await _engine.GetExecutionProfileAsync();
        var profile2 = await _engine.GetExecutionProfileAsync();

        // Assert
        profile1.Should().BeSameAs(profile2);
    }

    [Fact]
    public void GetCachedProfile_ReturnsNullBeforeDetection()
    {
        // Arrange
        var freshEngine = new AdaptiveExecutionEngine(new EnvironmentDetector(), new ScannerConfigurationService());

        // Act
        var cached = freshEngine.GetCachedProfile();

        // Assert
        cached.Should().BeNull();
    }

    [Fact]
    public async Task GetCachedProfile_ReturnsProfileAfterDetection()
    {
        // Arrange
        await _engine.GetExecutionProfileAsync();

        // Act
        var cached = _engine.GetCachedProfile();

        // Assert
        cached.Should().NotBeNull();
    }

    [Fact]
    public void GetEffectiveMaxDegreeOfParallelism_ReturnsPositiveValue()
    {
        // Act
        var parallelism = _engine.GetEffectiveMaxDegreeOfParallelism();

        // Assert
        parallelism.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetEffectiveMaxDegreeOfParallelism_UsesCachedProfile()
    {
        // Arrange
        await _engine.GetExecutionProfileAsync();

        // Act
        var parallelism = _engine.GetEffectiveMaxDegreeOfParallelism();

        // Assert
        parallelism.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ShouldSkipCheck_ReturnsFalseForValidCheck()
    {
        // Arrange
        await _engine.GetExecutionProfileAsync();

        // Act
        var shouldSkip = _engine.ShouldSkipCheck("VALID-CHECK");

        // Assert
        shouldSkip.Should().BeFalse();
    }

    [Fact]
    public async Task GetExecutionProfileAsync_TimeoutsArePositive()
    {
        // Act
        var profile = await _engine.GetExecutionProfileAsync();

        // Assert
        profile.EffectiveCheckTimeout.Should().BeGreaterThan(TimeSpan.Zero);
        profile.EffectiveParserTimeout.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task GetExecutionProfileAsync_ReasoningContainsParallelismInfo()
    {
        // Act
        var profile = await _engine.GetExecutionProfileAsync();

        // Assert
        profile.Reasoning.Should().Contain(r => r.Contains("Parallelism"));
    }

    [Fact]
    public async Task GetExecutionProfileAsync_ReasoningContainsTimeoutInfo()
    {
        // Act
        var profile = await _engine.GetExecutionProfileAsync();

        // Assert
        profile.Reasoning.Should().Contain(r => r.Contains("Timeouts"));
    }

    [Fact]
    public async Task GetExecutionProfileAsync_ReasoningContainsSkipInfo()
    {
        // Act
        var profile = await _engine.GetExecutionProfileAsync();

        // Assert
        profile.Reasoning.Should().Contain(r =>
            r.Contains("Skip") || r.Contains("No checks will be skipped"));
    }

    [Fact]
    public async Task HasSkippedChecks_ReflectsActualState()
    {
        // Act
        var profile = await _engine.GetExecutionProfileAsync();

        // Assert
        profile.HasSkippedChecks.Should().Be(profile.ChecksToSkip.Count > 0);
    }

    [Fact]
    public async Task IsReducedParallelismMode_ReflectsParallelismValue()
    {
        // Act
        var profile = await _engine.GetExecutionProfileAsync();

        // Assert
        profile.IsReducedParallelismMode.Should().Be(profile.EffectiveMaxDegreeOfParallelism == 1);
    }

    [Fact]
    public async Task WithCustomParallelism_RespectsUserConfiguration()
    {
        // Arrange
        var config = new ScannerConfiguration
        {
            MaxDegreeOfParallelism = 4,
            CheckTimeoutSeconds = 60,
            ParserTimeoutSeconds = 30
        };
        var configService = new ScannerConfigurationService(config);
        var engine = new AdaptiveExecutionEngine(_detector, configService);

        // Act
        var profile = await engine.GetExecutionProfileAsync();

        // Assert
        profile.EffectiveMaxDegreeOfParallelism.Should().Be(4);
        profile.Reasoning.Should().Contain(r => r.Contains("user-configured"));
    }

    [Fact]
    public async Task WithZeroParallelism_UsesEnvironmentBased()
    {
        // Arrange
        var config = new ScannerConfiguration
        {
            MaxDegreeOfParallelism = 0, // Auto-detect
            CheckTimeoutSeconds = 60,
            ParserTimeoutSeconds = 30
        };
        var configService = new ScannerConfigurationService(config);
        var engine = new AdaptiveExecutionEngine(_detector, configService);

        // Act
        var profile = await engine.GetExecutionProfileAsync();

        // Assert
        profile.EffectiveMaxDegreeOfParallelism.Should().BeGreaterThan(0);
        profile.Reasoning.Should().Contain(r => r.Contains("tier"));
    }

    [Fact]
    public async Task ProfileContainsMachineInfo()
    {
        // Act
        var profile = await _engine.GetExecutionProfileAsync();

        // Assert
        profile.Reasoning.Should().Contain(r => r.Contains(Environment.MachineName));
    }

    [Fact]
    public void ScanExecutionProfile_ComputedProperties_WorkCorrectly()
    {
        // Arrange
        var profile = new ScanExecutionProfile
        {
            EffectiveMaxDegreeOfParallelism = 1,
            EffectiveCheckTimeout = TimeSpan.FromSeconds(60),
            EffectiveParserTimeout = TimeSpan.FromSeconds(30),
            ChecksToSkip = new List<string> { "AUD-001" },
            Reasoning = new List<string> { "Test reasoning" },
            ComputedAt = DateTimeOffset.UtcNow
        };

        // Assert
        profile.HasSkippedChecks.Should().BeTrue();
        profile.IsReducedParallelismMode.Should().BeTrue();
    }

    [Fact]
    public void ScanExecutionProfile_ComputedProperties_WorkCorrectly_NoSkips()
    {
        // Arrange
        var profile = new ScanExecutionProfile
        {
            EffectiveMaxDegreeOfParallelism = 8,
            EffectiveCheckTimeout = TimeSpan.FromSeconds(60),
            EffectiveParserTimeout = TimeSpan.FromSeconds(30),
            ChecksToSkip = new List<string>(),
            Reasoning = new List<string> { "Test reasoning" },
            ComputedAt = DateTimeOffset.UtcNow
        };

        // Assert
        profile.HasSkippedChecks.Should().BeFalse();
        profile.IsReducedParallelismMode.Should().BeFalse();
    }
}