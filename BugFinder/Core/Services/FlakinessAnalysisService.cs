using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-07.1: Flaky Test Detection Engine
/// Analyzes execution history to identify flaky tests and calculate reliability scores.
/// </summary>
public class FlakinessAnalysisService
{
    private readonly Dictionary<string, TestReliabilityProfile> _profiles = new();

    /// <summary>
    /// Records a test execution result for later analysis.
    /// </summary>
    public void RecordExecution(string testId, TestOutcome outcome, string? errorMessage, long durationMs, string? envInfo = null)
    {
        if (!_profiles.ContainsKey(testId))
        {
            _profiles[testId] = new TestReliabilityProfile { TestId = testId };
        }

        var record = new ExecutionRecord
        {
            Outcome = outcome,
            ErrorMessage = errorMessage,
            DurationMs = durationMs,
            EnvironmentInfo = envInfo
        };

        _profiles[testId].AddExecution(record);
    }

    /// <summary>
    /// Gets the reliability profile for a specific test.
    /// </summary>
    public TestReliabilityProfile? GetProfile(string testId)
    {
        return _profiles.TryGetValue(testId, out var profile) ? profile : null;
    }

    /// <summary>
    /// Returns all tests categorized as flaky (Moderately or Highly).
    /// </summary>
    public List<TestReliabilityProfile> GetFlakyTests()
    {
        return _profiles.Values
            .Where(p => p.Category == FlakinessCategory.HighlyFlaky ||
                        p.Category == FlakinessCategory.ModeratelyFlaky)
            .OrderByDescending(p => p.Score.Value)
            .ToList();
    }

    /// <summary>
    /// Returns a summary of reliability across all tracked tests.
    /// </summary>
    public ReliabilitySummary GetSummary()
    {
        var total = _profiles.Count;
        if (total == 0) return new ReliabilitySummary();

        var stable = _profiles.Values.Count(p => p.Category == FlakinessCategory.Stable);
        var flaky = _profiles.Values.Count(p => p.Category == FlakinessCategory.HighlyFlaky || p.Category == FlakinessCategory.ModeratelyFlaky);
        var failing = _profiles.Values.Count(p => p.Category == FlakinessCategory.ConsistentlyFailing);

        return new ReliabilitySummary
        {
            TotalTestsTracked = total,
            StableTests = stable,
            FlakyTests = flaky,
            ConsistentlyFailingTests = failing,
            OverallFlakinessRate = total > 0 ? (double)flaky / total : 0.0
        };
    }
}

public class ReliabilitySummary
{
    public int TotalTestsTracked { get; set; }
    public int StableTests { get; set; }
    public int FlakyTests { get; set; }
    public int ConsistentlyFailingTests { get; set; }
    public double OverallFlakinessRate { get; set; }
}