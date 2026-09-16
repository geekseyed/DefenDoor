using ISCM.Application.Analytics;

namespace ISCM.Application.Interfaces;

/// <summary>
/// Service for calculating executive-level KPIs from historical scan data.
/// Phase 16.5: Provides high-level metrics for management dashboards.
/// </summary>
public interface IExecutiveKpiService
{
    /// <summary>
    /// Calculates comprehensive KPIs for the executive dashboard.
    /// Aggregates data from all historical snapshots.
    /// </summary>
    /// <param name="hostname">Optional hostname filter. If null, aggregates across all assets.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Complete KPI snapshot for dashboard display</returns>
    Task<ExecutiveKpi> CalculateKpisAsync(string? hostname = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates KPIs for a specific time window.
    /// </summary>
    /// <param name="fromDate">Start of time window (inclusive)</param>
    /// <param name="toDate">End of time window (inclusive)</param>
    /// <param name="hostname">Optional hostname filter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>KPIs for the specified time window</returns>
    Task<ExecutiveKpi> CalculateKpisForPeriodAsync(DateTime fromDate, DateTime toDate, string? hostname = null, CancellationToken cancellationToken = default);
}