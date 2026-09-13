namespace ISCM.Tests.Unit.Process;

using FluentAssertions;
using ISCM.Application.Interfaces;
using ISCM.Application.Services;
using ISCM.Domain.Entities;
using ISCM.Domain.ValueObjects;
using ISCM.Infrastructure.Scanning;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Phase 14.3: Unit tests for ProcessRunner and CachedProcessRunner.
/// Uses real process execution with simple echo commands.
/// </summary>
public class ProcessCachingTests
{
    private readonly IProcessRunner _processRunner;
    private readonly IProcessCacheService _cacheService;

    public ProcessCachingTests()
    {
        _processRunner = new ProcessRunner();
        var configService = new ScannerConfigurationService(new ScannerConfiguration
        {
            CacheMaxAgeMinutes = 5 // Long TTL for tests
        });
        _cacheService = new CachedProcessRunner(_processRunner, configService);
    }

    [Fact]
    public async Task ProcessRunner_ExecutesEchoCommand_ReturnsOutput()
    {
        // Act
        var result = await _processRunner.RunAsync("cmd", "/c echo Hello");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("Hello");
        result.DurationMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ProcessRunner_InvalidCommand_ReturnsErrorResult()
    {
        // Act
        var result = await _processRunner.RunAsync("nonexistent_command_xyz", "arg1");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ExitCode.Should().NotBe(0);
        result.ErrorOutput.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ProcessRunner_CombinedOutput_ReturnsOutputOnSuccess()
    {
        // Act
        var result = await _processRunner.RunAsync("cmd", "/c echo Test");

        // Assert
        result.CombinedOutput.Should().Be(result.Output);
        result.OutputOrError.Should().Be(result.Output);
    }

    [Fact]
    public async Task ProcessRunner_CombinedOutput_IncludesErrorOnFailure()
    {
        // Act
        var result = await _processRunner.RunAsync("nonexistent_command_xyz", "");

        // Assert
        result.CombinedOutput.Should().Contain("STDERR");
        result.OutputOrError.Should().Contain("Error");
    }

    [Fact]
    public async Task CachedProcessRunner_FirstCall_ExecutesProcess()
    {
        // Arrange
        var uniqueKey = Guid.NewGuid().ToString("N");

        // Act
        var result = await _cacheService.GetOrRunAsync("cmd", $"/c echo {uniqueKey}");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();

        var stats = _cacheService.GetStats();
        stats.CacheMisses.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task CachedProcessRunner_SecondCall_UsesCache()
    {
        // Arrange
        var uniqueKey = Guid.NewGuid().ToString("N");

        // Act - First call
        var sw1 = Stopwatch.StartNew();
        var result1 = await _cacheService.GetOrRunAsync("cmd", $"/c echo {uniqueKey}");
        sw1.Stop();

        // Act - Second call (should be cached)
        var sw2 = Stopwatch.StartNew();
        var result2 = await _cacheService.GetOrRunAsync("cmd", $"/c echo {uniqueKey}");
        sw2.Stop();

        // Assert
        result1.Output.Should().Be(result2.Output);
        result1.Command.Should().Be(result2.Command);
        result1.Arguments.Should().Be(result2.Arguments);

        var stats = _cacheService.GetStats();
        stats.CacheHits.Should().BeGreaterOrEqualTo(1);
        stats.TotalSavedMs.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task CachedProcessRunner_DifferentArguments_NotCachedTogether()
    {
        // Arrange
        var key1 = Guid.NewGuid().ToString("N");
        var key2 = Guid.NewGuid().ToString("N");

        // Act
        await _cacheService.GetOrRunAsync("cmd", $"/c echo {key1}");
        await _cacheService.GetOrRunAsync("cmd", $"/c echo {key2}");

        // Assert
        var stats = _cacheService.GetStats();
        stats.CacheHits.Should().Be(0);
        stats.CacheMisses.Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task CachedProcessRunner_Invalidate_RemovesFromCache()
    {
        // Arrange
        var uniqueKey = Guid.NewGuid().ToString("N");
        await _cacheService.GetOrRunAsync("cmd", $"/c echo {uniqueKey}");

        // Verify it's cached
        _cacheService.IsCached("cmd", $"/c echo {uniqueKey}").Should().BeTrue();

        // Act
        _cacheService.Invalidate("cmd");

        // Assert
        _cacheService.IsCached("cmd", $"/c echo {uniqueKey}").Should().BeFalse();
    }

    [Fact]
    public async Task CachedProcessRunner_InvalidateAll_ClearsEntireCache()
    {
        // Arrange
        var key1 = Guid.NewGuid().ToString("N");
        var key2 = Guid.NewGuid().ToString("N");
        await _cacheService.GetOrRunAsync("cmd", $"/c echo {key1}");
        await _cacheService.GetOrRunAsync("net", "accounts");

        // Act
        _cacheService.InvalidateAll();

        // Assert
        _cacheService.IsCached("cmd", $"/c echo {key1}").Should().BeFalse();
        _cacheService.IsCached("net", "accounts").Should().BeFalse();

        var stats = _cacheService.GetStats();
        stats.TotalEntries.Should().Be(0);
    }

    [Fact]
    public async Task CachedProcessRunner_ForceRefresh_IgnoresCache()
    {
        // Arrange
        var uniqueKey = Guid.NewGuid().ToString("N");
        await _cacheService.GetOrRunAsync("cmd", $"/c echo {uniqueKey}");

        var statsBefore = _cacheService.GetStats();
        var hitsBefore = statsBefore.CacheHits;

        // Act - Force refresh
        await _cacheService.GetOrRunAsync("cmd", $"/c echo {uniqueKey}", forceRefresh: true);

        // Assert
        var statsAfter = _cacheService.GetStats();
        statsAfter.CacheMisses.Should().Be(statsBefore.CacheMisses + 1);
        statsAfter.CacheHits.Should().Be(hitsBefore); // No additional hit
    }

    [Fact]
    public async Task CachedProcessRunner_FailedResult_NotCached()
    {
        // Act - Run a command that fails
        var result1 = await _cacheService.GetOrRunAsync("cmd", "/c exit 1");
        var result2 = await _cacheService.GetOrRunAsync("cmd", "/c exit 1");

        // Assert - Both should be cache misses (failed results not cached)
        var stats = _cacheService.GetStats();
        stats.CacheHits.Should().Be(0);
        stats.CacheMisses.Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public void CachedProcessRunner_GetStats_ReturnsValidStructure()
    {
        // Act
        var stats = _cacheService.GetStats();

        // Assert
        stats.Should().NotBeNull();
        stats.TotalEntries.Should().BeGreaterOrEqualTo(0);
        stats.CacheHits.Should().BeGreaterOrEqualTo(0);
        stats.CacheMisses.Should().BeGreaterOrEqualTo(0);
        stats.HitRate.Should().BeGreaterOrEqualTo(0);
        stats.HitRate.Should().BeLessOrEqualTo(1);
    }

    [Fact]
    public void CachedProcessRunner_HitRate_CalculatesCorrectly()
    {
        // Arrange - Empty cache has 0 rate
        var stats = _cacheService.GetStats();

        // Assert
        stats.TotalRequests.Should().Be(0);
        stats.HitRate.Should().Be(0);
    }

    [Fact]
    public async Task CachedProcessRunner_CaseInsensitiveCommand_MatchesSameKey()
    {
        // Arrange
        var uniqueKey = Guid.NewGuid().ToString("N");

        // Act - Call with uppercase command
        await _cacheService.GetOrRunAsync("CMD", $"/c echo {uniqueKey}");
        await _cacheService.GetOrRunAsync("cmd", $"/c echo {uniqueKey}");

        // Assert - Should be cached (case-insensitive command matching)
        var stats = _cacheService.GetStats();
        stats.CacheHits.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task ProcessResult_ComputedProperties_WorkCorrectly()
    {
        // Arrange
        var successResult = new ProcessResult
        {
            Output = "test output",
            ErrorOutput = "",
            ExitCode = 0,
            DurationMs = 100,
            ExecutedAt = DateTimeOffset.UtcNow,
            Command = "cmd",
            Arguments = "/c echo test"
        };

        var failureResult = new ProcessResult
        {
            Output = "partial output",
            ErrorOutput = "something failed",
            ExitCode = 1,
            DurationMs = 50,
            ExecutedAt = DateTimeOffset.UtcNow,
            Command = "cmd",
            Arguments = "/c invalid"
        };

        // Assert - Success case
        successResult.Success.Should().BeTrue();
        successResult.CombinedOutput.Should().Be("test output");
        successResult.OutputOrError.Should().Be("test output");

        // Assert - Failure case
        failureResult.Success.Should().BeFalse();
        failureResult.CombinedOutput.Should().Contain("STDERR");
        failureResult.OutputOrError.Should().Contain("Error");
    }
}