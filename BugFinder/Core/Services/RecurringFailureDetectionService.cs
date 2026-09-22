using ISCM.BugFinder.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-11.3: Recurring Failure Detection Service
/// Analyzes failure history to detect patterns and recurring issues.
/// </summary>
public class RecurringFailureDetectionService
{
    private readonly FailureHistoryService _historyService;
    private readonly LocationHistoryService _locationService;
    private readonly RecurrenceConfig _config;

    public RecurringFailureDetectionService(
        FailureHistoryService historyService,
        LocationHistoryService locationService,
        RecurrenceConfig? config = null)
    {
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        _locationService = locationService ?? throw new ArgumentNullException(nameof(locationService));
        _config = config ?? new RecurrenceConfig();
    }

    /// <summary>
    /// BF-11.3 - Main Entry Point:
    /// Analyzes a failure signature to determine if it's recurring and identifies the pattern.
    /// </summary>
    public async Task<RecurringFailureAnalysisResult> AnalyzeRecurrenceAsync(string failureSignature)
    {
        if (string.IsNullOrEmpty(failureSignature))
        {
            return new RecurringFailureAnalysisResult
            {
                AnalysisSummary = "Empty failure signature provided."
            };
        }

        var result = new RecurringFailureAnalysisResult
        {
            FailureSignature = failureSignature
        };

        // 1. Retrieve failure history
        var history = await _historyService.GetHistoryAsync(failureSignature);
        result.TotalOccurrences = history.TotalOccurrences;
        result.MatchingRecords = history.RecentOccurrences;

        if (history.TotalOccurrences == 0)
        {
            result.IsRecurring = false;
            result.DetectedPattern = RecurrencePattern.None;
            result.RecurrenceScore = 0.0;
            result.AnalysisSummary = "First time observing this failure signature.";
            return result;
        }

        result.IsRecurring = history.IsRecurring;

        // 2. Calculate Recurrence Score
        result.RecurrenceScore = CalculateRecurrenceScore(history);

        // 3. Detect Pattern
        result.DetectedPattern = DetectPattern(history);

        // 4. Generate Summary
        result.AnalysisSummary = GenerateSummary(result);

        return result;
    }

    /// <summary>
    /// Calculates a score from 0.0 to 1.0 indicating likelihood of being a systemic recurring issue.
    /// </summary>
    private double CalculateRecurrenceScore(FailureHistoryResult history)
    {
        if (history.TotalOccurrences < 2) return 0.0;

        double score = 0.0;

        // Factor 1: Frequency (Logarithmic scale to prevent infinity)
        double freqScore = Math.Min(1.0, Math.Log10(history.TotalOccurrences) / 3.0);
        score += freqScore * 0.4;

        // Factor 2: Recency (Weight recent failures higher)
        // FIX: Safe access to TotalDays on nullable TimeSpan
        var daysSinceLastSeen = history.LastSeen.HasValue
            ? (DateTime.UtcNow - history.LastSeen.Value).TotalDays
            : 0.0;

        double recencyScore = daysSinceLastSeen < _config.DaysToConsiderRecent ? 1.0 : 0.5;
        score += recencyScore * 0.3;

        // Factor 3: Persistence (Span of time)
        // FIX: Safe access to TotalDays on nullable TimeSpan
        var totalSpanDays = (history.FirstSeen.HasValue && history.LastSeen.HasValue)
            ? (history.LastSeen.Value - history.FirstSeen.Value).TotalDays
            : 0.0;

        double persistenceScore = totalSpanDays > 7 ? 1.0 : 0.5;
        score += persistenceScore * 0.3;

        return Math.Min(1.0, score);
    }

    /// <summary>
    /// Identifies the type of recurrence pattern.
    /// </summary>
    private RecurrencePattern DetectPattern(FailureHistoryResult history)
    {
        if (history.TotalOccurrences < _config.MinimumOccurrencesForPattern)
        {
            return RecurrencePattern.Sporadic;
        }

        // FIX: Safe calculation of totalDays
        var totalDays = (history.FirstSeen.HasValue && history.LastSeen.HasValue)
            ? (history.LastSeen.Value - history.FirstSeen.Value).TotalDays
            : 0.0;

        if (totalDays == 0) return RecurrencePattern.Persistent;

        double density = history.TotalOccurrences / totalDays;

        if (density > 1.0) // More than 1 failure per day on average
        {
            return RecurrencePattern.Persistent;
        }
        else if (density > 0.2) // Roughly once every 5 days
        {
            return RecurrencePattern.Periodic;
        }
        else
        {
            return RecurrencePattern.Sporadic;
        }
    }

    private string GenerateSummary(RecurringFailureAnalysisResult result)
    {
        if (!result.IsRecurring)
            return "New failure detected. No historical matches found.";

        var patternText = result.DetectedPattern switch
        {
            RecurrencePattern.Persistent => "consistently occurring",
            RecurrencePattern.Periodic => "periodically recurring",
            RecurrencePattern.Sporadic => "sporadically appearing",
            RecurrencePattern.Regressed => "regressed after a fix",
            _ => "unknown pattern"
        };

        return $"This is a {patternText} issue with {result.TotalOccurrences} occurrences " +
               $"and a confidence score of {result.RecurrenceScore:P1}.";
    }
}