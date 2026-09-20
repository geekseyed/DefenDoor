using ISCM.BugFinder.Core.Models;
using System.Linq;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-09.2: Trend Analysis Engine
/// Analyzes historical test data to identify degradation trends (Time, Stability, Frequency).
/// </summary>
public class TrendAnalysisService
{
    /// <summary>
    /// Analyzes the trend of a specific test over time.
    /// </summary>
    public TrendAnalysisResult AnalyzeTrend(List<TestHistoryRecord> history)
    {
        if (history == null || history.Count < 3)
        {
            return new TrendAnalysisResult
            {
                Status = StabilityStatus.InsufficientData,
                Message = "At least 3 historical records are required for trend analysis."
            };
        }

        // Sort by date ascending
        var sorted = history.OrderBy(h => h.ExecutedAt).ToList();

        // 1. Analyze Duration Trend (Convert long to double for regression)
        var durations = sorted.Select(h => (double)h.DurationMs).ToList();
        var durationTrend = CalculateLinearRegression(durations);

        // 2. Analyze Failure Rate Trend (Sliding Window)
        int windowSize = 5;
        var failureRates = new List<double>();

        for (int i = 0; i <= sorted.Count - windowSize; i++)
        {
            var window = sorted.Skip(i).Take(windowSize);
            double failRate = window.Count(h => h.WasSuccessful == false) / (double)windowSize;
            failureRates.Add(failRate);
        }

        var failureTrend = failureRates.Count >= 2 ? CalculateLinearRegression(failureRates) : 0.0;

        // Determine Overall Status
        var result = new TrendAnalysisResult
        {
            AverageDurationMs = durations.Average(),
            DurationTrendSlope = durationTrend,
            FailureRateTrendSlope = failureTrend,
            RecordCount = sorted.Count,
            FirstRunDate = sorted.First().ExecutedAt,
            LastRunDate = sorted.Last().ExecutedAt
        };

        if (durationTrend > 10.0) // Increasing by >10ms per run
        {
            result.Status = StabilityStatus.DegradingPerformance;
            result.Message = $"Test duration is increasing by {durationTrend:F2}ms per run.";
        }
        else if (failureTrend > 0.05) // Failure rate increasing by >5% per window
        {
            result.Status = StabilityStatus.DegradingStability;
            result.Message = $"Failure rate is trending upwards ({failureTrend:F2} per window).";
        }
        else if (failureTrend < -0.05 && durationTrend < -5.0)
        {
            result.Status = StabilityStatus.Improving;
            result.Message = "Test performance and stability are improving.";
        }
        else
        {
            result.Status = StabilityStatus.Stable;
            result.Message = "No significant degradation or improvement detected.";
        }

        return result;
    }

    /// <summary>
    /// Simple Linear Regression to find the slope (trend direction).
    /// Returns the slope (change in Y per unit X).
    /// </summary>
    private double CalculateLinearRegression(List<double> values)
    {
        int n = values.Count;
        if (n < 2) return 0.0;

        double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;

        for (int i = 0; i < n; i++)
        {
            double x = i;
            double y = values[i];
            sumX += x;
            sumY += y;
            sumXY += x * y;
            sumXX += x * x;
        }

        double denominator = (n * sumXX) - (sumX * sumX);
        if (denominator == 0) return 0.0;

        return ((n * sumXY) - (sumX * sumY)) / denominator;
    }
}