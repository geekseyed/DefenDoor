namespace ISCM.Tests.Unit.Configuration;

using FluentAssertions;
using ISCM.Application.Interfaces;
using ISCM.Application.Services;
using ISCM.Domain.Entities;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using Xunit;

/// <summary>
/// Phase 14.4: Unit tests for configuration profile binding.
/// Tests that ScannerConfigurationService correctly reads from IConfiguration.
/// </summary>
public class ConfigurationProfileTests
{
    [Fact]
    public void ScannerConfigurationService_ReadsFromConfiguration()
    {
        // Arrange
        var configData = new Dictionary<string, string?>
        {
            { "Scanner:CacheMaxAgeMinutes", "45" },
            { "Scanner:ParserTimeoutSeconds", "15" },
            { "Scanner:CheckTimeoutSeconds", "25" },
            { "Scanner:VerboseLogging", "true" }
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // Act
        var service = new ScannerConfigurationService(configuration);
        var config = service.GetCurrentConfiguration();

        // Assert
        config.CacheMaxAgeMinutes.Should().Be(45);
        config.ParserTimeoutSeconds.Should().Be(15);
        config.CheckTimeoutSeconds.Should().Be(25);
        config.VerboseLogging.Should().BeTrue();
    }

    [Fact]
    public void ScannerConfigurationService_UsesDefaults_WhenSectionMissing()
    {
        // Arrange - Empty configuration (no Scanner section)
        var configuration = new ConfigurationBuilder().Build();

        // Act
        var service = new ScannerConfigurationService(configuration);
        var config = service.GetCurrentConfiguration();

        // Assert - Should use defaults from ScannerConfiguration class
        config.CacheMaxAgeMinutes.Should().Be(30); // Default
        config.ParserTimeoutSeconds.Should().Be(30); // Default
        config.CheckTimeoutSeconds.Should().Be(60); // Default
    }

    [Fact]
    public void ScannerConfigurationService_DevelopmentProfile_HasShortTimeouts()
    {
        // Arrange - Development profile values
        var configData = new Dictionary<string, string?>
        {
            { "Scanner:CacheMaxAgeMinutes", "5" },
            { "Scanner:ParserTimeoutSeconds", "10" },
            { "Scanner:CheckTimeoutSeconds", "20" },
            { "Scanner:VerboseLogging", "true" },
            { "Scanner:LogRawOutput", "true" }
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // Act
        var service = new ScannerConfigurationService(configuration);

        // Assert
        service.GetCacheMaxAge().Should().Be(System.TimeSpan.FromMinutes(5));
        service.GetParserTimeoutSeconds().Should().Be(10);
        service.GetCheckTimeoutSeconds().Should().Be(20);
    }

    [Fact]
    public void ScannerConfigurationService_ProductionProfile_HasLongTimeouts()
    {
        // Arrange - Production profile values
        var configData = new Dictionary<string, string?>
        {
            { "Scanner:CacheMaxAgeMinutes", "60" },
            { "Scanner:MaxScanDurationMinutes", "120" },
            { "Scanner:ParserTimeoutSeconds", "60" },
            { "Scanner:CheckTimeoutSeconds", "120" },
            { "Scanner:VerboseLogging", "false" },
            { "Scanner:LogRawOutput", "false" }
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // Act
        var service = new ScannerConfigurationService(configuration);

        // Assert
        service.GetCacheMaxAge().Should().Be(System.TimeSpan.FromMinutes(60));
        service.GetParserTimeoutSeconds().Should().Be(60);
        service.GetCheckTimeoutSeconds().Should().Be(120);
    }

    [Fact]
    public void ScannerConfigurationService_AirGappedProfile_SequentialExecution()
    {
        // Arrange - AirGapped profile values
        var configData = new Dictionary<string, string?>
        {
            { "Scanner:CacheMaxAgeMinutes", "1440" },
            { "Scanner:MaxDegreeOfParallelism", "1" },
            { "Scanner:CheckTimeoutSeconds", "300" },
            { "Scanner:EnableFingerprintValidation", "false" }
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // Act
        var service = new ScannerConfigurationService(configuration);
        var config = service.GetCurrentConfiguration();

        // Assert
        service.GetMaxDegreeOfParallelism().Should().Be(1);
        service.GetCacheMaxAge().Should().Be(System.TimeSpan.FromMinutes(1440));
        service.GetCheckTimeoutSeconds().Should().Be(300);
        service.IsFingerprintValidationEnabled().Should().BeFalse();
    }

    [Fact]
    public void ScannerConfigurationService_BooleanParsing_HandlesTrueAndFalse()
    {
        // Arrange
        var configData = new Dictionary<string, string?>
        {
            { "Scanner:EnableCache", "false" },
            { "Scanner:EnableFingerprintValidation", "false" },
            { "Scanner:EnableFreshnessPolicy", "true" },
            { "Scanner:VerboseLogging", "true" }
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // Act
        var service = new ScannerConfigurationService(configuration);

        // Assert
        service.IsCacheEnabled().Should().BeFalse();
        service.IsFingerprintValidationEnabled().Should().BeFalse();
        service.GetCurrentConfiguration().EnableFreshnessPolicy.Should().BeTrue();
        service.GetCurrentConfiguration().VerboseLogging.Should().BeTrue();
    }

    [Fact]
    public void ScannerConfigurationService_InvalidValues_KeepDefaults()
    {
        // Arrange - Invalid values that should be ignored
        var configData = new Dictionary<string, string?>
        {
            { "Scanner:CacheMaxAgeMinutes", "not_a_number" },
            { "Scanner:VerboseLogging", "maybe" }
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // Act
        var service = new ScannerConfigurationService(configuration);
        var config = service.GetCurrentConfiguration();

        // Assert - Should keep defaults when parsing fails
        config.CacheMaxAgeMinutes.Should().Be(30); // Default
        config.VerboseLogging.Should().BeFalse(); // Default
    }

    [Fact]
    public void ScannerConfigurationService_UpdateConfiguration_OverridesSettings()
    {
        // Arrange
        var configData = new Dictionary<string, string?>
        {
            { "Scanner:CacheMaxAgeMinutes", "30" }
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        var service = new ScannerConfigurationService(configuration);

        // Act - Update configuration
        var newConfig = new ScannerConfiguration
        {
            CacheMaxAgeMinutes = 90,
            ParserTimeoutSeconds = 45
        };
        service.UpdateConfiguration(newConfig);

        // Assert
        service.GetCacheMaxAge().Should().Be(System.TimeSpan.FromMinutes(90));
        service.GetParserTimeoutSeconds().Should().Be(45);
    }

    [Fact]
    public void ScannerConfigurationService_DefaultConstructor_UsesDefaults()
    {
        // Arrange - No IConfiguration, uses parameterless constructor
        var service = new ScannerConfigurationService();

        // Act
        var config = service.GetCurrentConfiguration();

        // Assert - All defaults
        config.CacheMaxAgeMinutes.Should().Be(30);
        config.ParserTimeoutSeconds.Should().Be(30);
        config.CheckTimeoutSeconds.Should().Be(60);
        config.MaxDegreeOfParallelism.Should().Be(0); // 0 = auto-detect
        config.EnableCache.Should().BeTrue();
        config.VerboseLogging.Should().BeFalse();
    }

    [Fact]
    public void ScannerConfigurationService_MaxDegreeOfParallelism_ZeroMeansAuto()
    {
        // Arrange
        var configData = new Dictionary<string, string?>
        {
            { "Scanner:MaxDegreeOfParallelism", "0" }
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // Act
        var service = new ScannerConfigurationService(configuration);

        // Assert - 0 means auto-detect (Environment.ProcessorCount)
        service.GetMaxDegreeOfParallelism().Should().Be(System.Environment.ProcessorCount);
    }

    [Fact]
    public void ScannerConfigurationService_MaxDegreeOfParallelism_ExplicitValue()
    {
        // Arrange
        var configData = new Dictionary<string, string?>
        {
            { "Scanner:MaxDegreeOfParallelism", "4" }
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        // Act
        var service = new ScannerConfigurationService(configuration);

        // Assert
        service.GetMaxDegreeOfParallelism().Should().Be(4);
    }
}