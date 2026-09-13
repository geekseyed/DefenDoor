namespace ISCM.Tests.Unit.Environment;

using FluentAssertions;
using ISCM.Application.Interfaces;
using ISCM.Domain.Enums;
using ISCM.Domain.ValueObjects;
using ISCM.Infrastructure.Scanning.Collectors;
using System;
using Xunit;

/// <summary>
/// Phase 14.1: Unit tests for EnvironmentDetector.
/// These are integration-style tests that run on real environment.
/// </summary>
public class EnvironmentDetectorTests
{
    private readonly IEnvironmentDetector _detector;

    public EnvironmentDetectorTests()
    {
        _detector = new EnvironmentDetector();
    }

    [Fact]
    public async Task DetectEnvironmentAsync_ReturnsValidProfile()
    {
        // Act
        var profile = await _detector.DetectEnvironmentAsync();

        // Assert
        profile.Should().NotBeNull();
        profile.DetectedAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
        profile.Permissions.Should().NotBeNull();
        profile.AvailableTools.Should().NotBeNull();
        profile.Hardware.Should().NotBeNull();
        profile.Warnings.Should().NotBeNull();
    }

    [Fact]
    public async Task DetectEnvironmentAsync_PopulatesHardwareInfo()
    {
        // Act
        var profile = await _detector.DetectEnvironmentAsync();

        // Assert
        profile.Hardware.ProcessorCount.Should().BeGreaterThan(0);
        profile.Hardware.MachineName.Should().NotBeNullOrEmpty();
        profile.Hardware.MachineName.Should().Be(Environment.MachineName);
    }

    [Fact]
    public async Task DetectEnvironmentAsync_DeterminesPerformanceTier()
    {
        // Act
        var profile = await _detector.DetectEnvironmentAsync();

        // Assert
        profile.PerformanceTier.Should().BeOneOf(
            PerformanceTier.Fast,
            PerformanceTier.Medium,
            PerformanceTier.Slow);
    }

    [Fact]
    public async Task DetectEnvironmentAsync_CachesResult()
    {
        // Act
        var profile1 = await _detector.DetectEnvironmentAsync();
        var profile2 = await _detector.DetectEnvironmentAsync();

        // Assert
        profile1.Should().BeSameAs(profile2);
    }

    [Fact]
    public void GetCachedProfile_ReturnsNullBeforeDetection()
    {
        // Arrange
        var freshDetector = new EnvironmentDetector();

        // Act
        var cached = freshDetector.GetCachedProfile();

        // Assert
        cached.Should().BeNull();
    }

    [Fact]
    public async Task GetCachedProfile_ReturnsProfileAfterDetection()
    {
        // Arrange
        var freshDetector = new EnvironmentDetector();
        await freshDetector.DetectEnvironmentAsync();

        // Act
        var cached = freshDetector.GetCachedProfile();

        // Assert
        cached.Should().NotBeNull();
    }

    [Theory]
    [InlineData(PermissionType.Administrator)]
    [InlineData(PermissionType.RegistryRead)]
    [InlineData(PermissionType.SeceditExecution)]
    [InlineData(PermissionType.PowerShellExecution)]
    public void HasPermission_ReturnsValidBool_WithoutFullDetection(PermissionType permission)
    {
        // Act
        var result = _detector.HasPermission(permission);

        // Assert - bool is always true or false, just verify no exception
        // We use IsType to confirm it's a valid bool
        Assert.IsType<bool>(result);
    }

    [Theory]
    [InlineData(ToolType.PowerShell)]
    [InlineData(ToolType.NetExe)]
    [InlineData(ToolType.Secedit)]
    [InlineData(ToolType.Auditpol)]
    public void IsToolAvailable_ReturnsValidBool_WithoutFullDetection(ToolType tool)
    {
        // Act
        var result = _detector.IsToolAvailable(tool);

        // Assert - bool is always true or false, just verify no exception
        Assert.IsType<bool>(result);
    }

    [Fact]
    public void GetPerformanceTier_ReturnsValidTier_WithoutFullDetection()
    {
        // Act
        var tier = _detector.GetPerformanceTier();

        // Assert
        tier.Should().BeOneOf(
            PerformanceTier.Fast,
            PerformanceTier.Medium,
            PerformanceTier.Slow);
    }

    [Fact]
    public async Task DetectEnvironmentAsync_PowerShellShouldBeAvailable()
    {
        // Act
        var profile = await _detector.DetectEnvironmentAsync();

        // Assert
        profile.AvailableTools.PowerShell.Should().BeTrue("PowerShell should be available on Windows");
        profile.AvailableTools.PowerShellVersion.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DetectEnvironmentAsync_NetExeShouldBeAvailable()
    {
        // Act
        var profile = await _detector.DetectEnvironmentAsync();

        // Assert
        profile.AvailableTools.NetExe.Should().BeTrue("net.exe should be available on Windows");
    }

    [Fact]
    public async Task DetectEnvironmentAsync_RegistryReadShouldWork()
    {
        // Act
        var profile = await _detector.DetectEnvironmentAsync();

        // Assert
        profile.Permissions.CanReadRegistry.Should().BeTrue("Should be able to read HKLM registry");
    }

    [Fact]
    public async Task DetectEnvironmentAsync_WarningsListIsNotNull()
    {
        // Act
        var profile = await _detector.DetectEnvironmentAsync();

        // Assert
        profile.Warnings.Should().NotBeNull();
    }

    [Fact]
    public void EnvironmentProfile_IsFullyCapable_CalculatesCorrectly()
    {
        // Arrange
        var profile = new EnvironmentProfile
        {
            Permissions = new PermissionProfile
            {
                IsAdmin = true,
                CanReadRegistry = true,
                CanWriteRegistry = true,
                CanRunSecedit = true,
                CanRunPowerShell = true
            },
            AvailableTools = new ToolAvailabilityProfile
            {
                PowerShell = true,
                PowerShellVersion = "5.1",
                NetExe = true,
                Secedit = true,
                Auditpol = true
            },
            PerformanceTier = PerformanceTier.Fast,
            Warnings = new List<string>(),
            DetectedAt = DateTimeOffset.UtcNow,
            Hardware = new HardwareProfile
            {
                ProcessorCount = 8,
                TotalMemoryMB = 16384,
                IsVirtualMachine = false,
                MachineName = "TestMachine"
            }
        };

        // Assert
        profile.IsFullyCapable.Should().BeTrue();
        profile.HasCriticalDeficiencies.Should().BeFalse();
    }

    [Fact]
    public void EnvironmentProfile_HasCriticalDeficiencies_WhenCannotReadRegistry()
    {
        // Arrange
        var profile = new EnvironmentProfile
        {
            Permissions = new PermissionProfile
            {
                IsAdmin = true,
                CanReadRegistry = false, // Critical deficiency
                CanWriteRegistry = true,
                CanRunSecedit = true,
                CanRunPowerShell = true
            },
            AvailableTools = new ToolAvailabilityProfile
            {
                PowerShell = true,
                PowerShellVersion = "5.1",
                NetExe = true,
                Secedit = true,
                Auditpol = true
            },
            PerformanceTier = PerformanceTier.Fast,
            Warnings = new List<string>(),
            DetectedAt = DateTimeOffset.UtcNow,
            Hardware = new HardwareProfile
            {
                ProcessorCount = 8,
                TotalMemoryMB = 16384,
                IsVirtualMachine = false,
                MachineName = "TestMachine"
            }
        };

        // Assert
        profile.HasCriticalDeficiencies.Should().BeTrue();
    }
}