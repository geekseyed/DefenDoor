using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-07.2: Retry Strategy & Reproduction Logic
/// Executes retry logic for flaky tests and generates reproduction scripts.
/// </summary>
public class RetryExecutionService
{
    private readonly FlakinessAnalysisService _flakinessService;

    public RetryExecutionService(FlakinessAnalysisService flakinessService)
    {
        _flakinessService = flakinessService;
    }

    /// <summary>
    /// Simulates a retry loop for a specific test failure.
    /// In a real implementation, this would invoke the test runner directly.
    /// Here we simulate outcomes based on the known reliability profile.
    /// </summary>
    public async Task<RetryResult> ExecuteWithRetryAsync(
        string testId,
        RetryStrategy strategy,
        Func<Task<TestOutcome>> testRunnerFunc)
    {
        var result = new RetryResult();

        // Check if we should retry based on condition
        var profile = _flakinessService.GetProfile(testId);
        if (strategy.Condition == RetryCondition.OnFlakyFailure &&
            profile != null && profile.Category != FlakinessCategory.HighlyFlaky &&
            profile.Category != FlakinessCategory.ModeratelyFlaky)
        {
            // Not flaky, just run once
            var outcome = await testRunnerFunc();
            result.IsSuccess = outcome == TestOutcome.Passed;
            result.AttemptsMade = 1;
            result.AttemptOutcomes.Add(outcome);
            return result;
        }

        // Retry loop
        for (int i = 0; i < strategy.MaxRetries; i++)
        {
            try
            {
                var outcome = await testRunnerFunc();
                result.AttemptOutcomes.Add(outcome);

                if (outcome == TestOutcome.Passed)
                {
                    result.IsSuccess = true;
                    result.AttemptsMade = i + 1;
                    return result; // Success, stop retrying
                }
            }
            catch (Exception ex)
            {
                result.AttemptOutcomes.Add(TestOutcome.Failed);
                result.FinalErrorMessage = ex.Message;
            }

            result.AttemptsMade = i + 1;

            // Delay before next retry
            if (i < strategy.MaxRetries - 1)
                await Task.Delay(strategy.DelayMs);
        }

        result.IsSuccess = false;
        return result;
    }

    /// <summary>
    /// Generates a shell command to reproduce a specific test failure manually.
    /// </summary>
    public ReproductionScript GenerateReproductionScript(string testId, string? errorMessage)
    {
        var script = new ReproductionScript
        {
            TestId = testId,
            Command = $"dotnet test --filter \"FullyQualifiedName~{testId}\" --logger \"console;verbosity=detailed\"",
            Notes = "Run this command multiple times to observe flakiness."
        };

        // Add environment hints if error suggests infrastructure issue
        if (!string.IsNullOrEmpty(errorMessage) && errorMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            script.EnvironmentVariables.Add("TEST_TIMEOUT_MULTIPLIER=2");
            script.Notes += " Timeout detected; consider increasing timeout multiplier.";
        }

        return script;
    }
}