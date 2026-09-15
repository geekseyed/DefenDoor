using ISCM.Application.Analytics;

namespace ISCM.Application.Interfaces;

/// <summary>
/// Service for analyzing historical compliance trends across scan snapshots.
/// </summary>
public interface ITrendAnalysisService
{
    /// <summary>
    /// Analyzes compliance trends for a specific asset (hostname).
    /// </summary>
    /// <param name="hostname">The asset hostname to analyze</param>
    /// <param name="maxDataPoints">Maximum number of historical points to include</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Trend analysis result with data points and statistics</returns>
    Task<TrendAnalysisResult> AnalyzeAssetTrendAsync(
        string hostname,
        int maxDataPoints = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes compliance trends across all assets (global view).
    /// </summary>
    /// <param name="maxDataPoints">Maximum number of historical points to include</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Trend analysis result with aggregated data points</returns>
    Task<TrendAnalysisResult> AnalyzeGlobalTrendAsync(
        int maxDataPoints = 50,
        CancellationToken cancellationToken = default);
}