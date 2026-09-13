using ISCM.Application.Interfaces;
using ISCM.Domain.Entities;
using Microsoft.Extensions.Configuration;

namespace ISCM.Application.Services;

/// <summary>
/// Phase 14.4: Updated to read from IConfiguration (appsettings.json).
/// Supports configuration profiles (Development, Production, AirGapped).
///
/// Configuration priority:
/// 1. appsettings.{Environment}.json (overrides)
/// 2. appsettings.json (base)
/// 3. Default values in ScannerConfiguration class
/// </summary>
public class ScannerConfigurationService : IScannerConfigurationService
{
    private ScannerConfiguration _currentConfiguration;
    private readonly IConfiguration? _configuration;

    /// <summary>
    /// Constructor for DI with IConfiguration binding.
    /// Reads "Scanner" section from appsettings.json.
    /// </summary>
    public ScannerConfigurationService(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _currentConfiguration = new ScannerConfiguration();
        BindFromConfiguration();
    }

    /// <summary>
    /// Constructor for tests or manual configuration (uses defaults).
    /// </summary>
    public ScannerConfigurationService()
    {
        _currentConfiguration = new ScannerConfiguration();
    }

    /// <summary>
    /// Constructor for tests with explicit configuration.
    /// </summary>
    public ScannerConfigurationService(ScannerConfiguration initialConfiguration)
    {
        _currentConfiguration = initialConfiguration ?? new ScannerConfiguration();
    }

    /// <summary>
    /// Phase 14.4: Binds ScannerConfiguration from IConfiguration "Scanner" section.
    /// Only overrides properties that exist in configuration.
    /// </summary>
    private void BindFromConfiguration()
    {
        if (_configuration == null)
            return;

        var section = _configuration.GetSection("Scanner");
        if (!section.Exists())
            return;

        // Bind individual properties with safe parsing
        if (int.TryParse(section[nameof(ScannerConfiguration.CacheMaxAgeMinutes)], out var cacheAge))
            _currentConfiguration.CacheMaxAgeMinutes = cacheAge;

        if (bool.TryParse(section[nameof(ScannerConfiguration.EnableCache)], out var enableCache))
            _currentConfiguration.EnableCache = enableCache;

        if (int.TryParse(section[nameof(ScannerConfiguration.MaxScanDurationMinutes)], out var maxScan))
            _currentConfiguration.MaxScanDurationMinutes = maxScan;

        if (int.TryParse(section[nameof(ScannerConfiguration.ParserTimeoutSeconds)], out var parserTimeout))
            _currentConfiguration.ParserTimeoutSeconds = parserTimeout;

        if (int.TryParse(section[nameof(ScannerConfiguration.CheckTimeoutSeconds)], out var checkTimeout))
            _currentConfiguration.CheckTimeoutSeconds = checkTimeout;

        if (int.TryParse(section[nameof(ScannerConfiguration.MaxDegreeOfParallelism)], out var parallelism))
            _currentConfiguration.MaxDegreeOfParallelism = parallelism;

        if (bool.TryParse(section[nameof(ScannerConfiguration.EnableFingerprintValidation)], out var fingerprint))
            _currentConfiguration.EnableFingerprintValidation = fingerprint;

        if (bool.TryParse(section[nameof(ScannerConfiguration.EnableFreshnessPolicy)], out var freshness))
            _currentConfiguration.EnableFreshnessPolicy = freshness;

        if (bool.TryParse(section[nameof(ScannerConfiguration.VerboseLogging)], out var verbose))
            _currentConfiguration.VerboseLogging = verbose;

        if (bool.TryParse(section[nameof(ScannerConfiguration.LogRawOutput)], out var logRaw))
            _currentConfiguration.LogRawOutput = logRaw;
    }

    public ScannerConfiguration GetCurrentConfiguration()
    {
        return _currentConfiguration;
    }

    public void UpdateConfiguration(ScannerConfiguration configuration)
    {
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));
        _currentConfiguration = configuration;
        _currentConfiguration.UpdateTimestamp();
    }

    public TimeSpan GetCacheMaxAge()
    {
        return TimeSpan.FromMinutes(_currentConfiguration.CacheMaxAgeMinutes);
    }

    public int GetParserTimeoutSeconds()
    {
        return _currentConfiguration.ParserTimeoutSeconds;
    }

    public int GetCheckTimeoutSeconds()
    {
        return _currentConfiguration.CheckTimeoutSeconds;
    }

    /// <summary>
    /// Phase 12.11: Returns the configured maximum degree of parallelism.
    /// If value is 0 or negative, defaults to Environment.ProcessorCount.
    /// </summary>
    public int GetMaxDegreeOfParallelism()
    {
        return _currentConfiguration.MaxDegreeOfParallelism > 0
            ? _currentConfiguration.MaxDegreeOfParallelism
            : Environment.ProcessorCount;
    }

    public bool IsCacheEnabled()
    {
        return _currentConfiguration.EnableCache;
    }

    public bool IsFingerprintValidationEnabled()
    {
        return _currentConfiguration.EnableFingerprintValidation;
    }
}