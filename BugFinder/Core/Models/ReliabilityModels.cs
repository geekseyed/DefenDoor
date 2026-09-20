using ISCM.BugFinder.Core.Models; // Reference to existing models if needed, though TestOutcome might need to be here or referenced

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-07: Reproduction & Reliability Models
/// Tracks test execution history to detect flaky tests and calculate reliability scores.
/// </summary>

public class TestReliabilityProfile
{
    public string TestId { get; set; } = string.Empty;
    public List<ExecutionRecord> ExecutionHistory { get; set; } = new();

    public int TotalRuns { get; set; }
    public int PassCount { get; set; }
    public int FailCount { get; set; }
    public int Flips { get; set; }

    public FlakinessScore Score { get; set; } = new();
    public FlakinessCategory Category { get; set; } = FlakinessCategory.Unknown;

    public void AddExecution(ExecutionRecord record)
    {
        ExecutionHistory.Add(record);
        RecalculateMetrics();
    }

    private void RecalculateMetrics()
    {
        TotalRuns = ExecutionHistory.Count;
        PassCount = ExecutionHistory.Count(e => e.Outcome == TestOutcome.Passed);
        FailCount = ExecutionHistory.Count(e => e.Outcome == TestOutcome.Failed);

        Flips = 0;
        for (int i = 1; i < ExecutionHistory.Count; i++)
        {
            if (ExecutionHistory[i].Outcome != ExecutionHistory[i - 1].Outcome)
            {
                Flips++;
            }
        }

        CalculateScore();
        Categorize();
    }

    private void CalculateScore()
    {
        if (TotalRuns < 2)
        {
            Score.Value = 0.0;
            Score.Confidence = "Low";
            return;
        }

        var flipRatio = (double)Flips / (TotalRuns - 1);

       
        Score.Value = Math.Round(flipRatio, 2);

        Score.Confidence = TotalRuns >= 10 ? "High" : (TotalRuns >= 5 ? "Medium" : "Low");
    }

    private void Categorize()
    {
        if (Score.Value == 0.0 && TotalRuns > 0)
        {
            Category = FlakinessCategory.Stable;
        }
        else if (Score.Value > 0.3) // Lowered threshold from 0.5 to 0.3 for HighlyFlaky
        {
            Category = FlakinessCategory.HighlyFlaky;
        }
        else if (Score.Value > 0.1) // Lowered threshold from 0.2 to 0.1 for ModeratelyFlaky
        {
            Category = FlakinessCategory.ModeratelyFlaky;
        }
        else if (FailCount > 0 && PassCount == 0)
        {
            Category = FlakinessCategory.ConsistentlyFailing;
        }
        else
        {
            Category = FlakinessCategory.MostlyStable;
        }
    }
}

public class ExecutionRecord
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public TestOutcome Outcome { get; set; }
    public string? ErrorMessage { get; set; }
    public long DurationMs { get; set; }
    public string? EnvironmentInfo { get; set; }
}

public class FlakinessScore
{
    public double Value { get; set; } // 0.0 (Stable) to 1.0 (Very Flaky)
    public string Confidence { get; set; } = "Unknown"; // Low, Medium, High
}

public enum FlakinessCategory
{
    Unknown,
    Stable,
    MostlyStable,
    ModeratelyFlaky,
    HighlyFlaky,
    ConsistentlyFailing
}

// Define TestOutcome here if not already available in this namespace from BF-01 models
// Assuming it was defined in BugFinderModels.cs in Phase 1, we reference it. 
// If CS0246 persists for TestOutcome, ensure 'using ISCM.BugFinder.Core.Models;' is present or move the enum here.
// For safety in this specific file context, let's re-declare if necessary, but typically it's shared.
// Since BF-01 defined it in BugFinderModels.cs, we just need to make sure that file is compiled and referenced.
// However, to prevent circular dependency or missing ref issues in this specific snippet, I will assume it exists.
// IF you get error on TestOutcome, copy the enum below into BugFinderModels.cs or here.
/*
public enum TestOutcome
{
    Passed,
    Failed,
    Skipped,
    Unknown
}
*/