using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// Orchestrates the ingestion of Test Results and Domain Evaluation Results into a unified Session.
/// BF-01.5: Result Normalization
/// </summary>
public class ResultIngestionService
{
    private readonly TrxResultParser _trxParser;
    private readonly ConsoleEvaluationParser _consoleParser;

    public ResultIngestionService()
    {
        _trxParser = new TrxResultParser();
        _consoleParser = new ConsoleEvaluationParser();
    }

    public BugFinderSession Ingest(TestExecutionResult executionResult)
    {
        // 1. Parse TRX for Test Results
        var session = _trxParser.Parse(executionResult.TrxFilePath);

        // 2. Parse Console for Domain Evaluations
        var evaluationResults = _consoleParser.Parse(executionResult.ConsoleOutput);
        session.EvaluationResults = evaluationResults;

        // 3. Attach Artifacts
        session.Artifacts.Add(executionResult.TrxFilePath);

        return session;
    }
}