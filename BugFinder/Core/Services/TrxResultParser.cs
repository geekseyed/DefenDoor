using System.Xml.Linq;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// Parses Visual Studio TRX files into normalized Bug Finder models.
/// BF-01.2: Test Result Collection
/// </summary>
public class TrxResultParser
{
    public BugFinderSession Parse(string trxFilePath)
    {
        var doc = XDocument.Load(trxFilePath);
        var ns = doc.Root?.Name.Namespace ?? throw new InvalidOperationException("Invalid TRX format");

        var session = new BugFinderSession
        {
            SessionId = Guid.NewGuid().ToString(),
            StartedAt = DateTime.UtcNow, // Approximate
            Artifacts = new List<string> { trxFilePath }
        };

        // 1. Parse Test Definitions (ID -> Name/Class mapping)
        var testDefinitions = new Dictionary<string, (string Name, string ClassName)>();
        var testDefsElement = doc.Root.Element(ns + "TestDefinitions");
        if (testDefsElement != null)
        {
            foreach (var unitTest in testDefsElement.Elements(ns + "UnitTest"))
            {
                var id = (string?)unitTest.Attribute("id") ?? "";
                var name = (string?)unitTest.Element(ns + "TestName")?.Value ?? "";
                var className = (string?)unitTest.Element(ns + "TestMethod")?.Attribute("className")?.Value ?? "";

                testDefinitions[id] = (name, className);
            }
        }

        // 2. Parse Results
        var testResultsElement = doc.Root.Element(ns + "Results");
        if (testResultsElement != null)
        {
            foreach (var unitTestResult in testResultsElement.Elements(ns + "UnitTestResult"))
            {
                var testId = (string?)unitTestResult.Attribute("testId") ?? "";
                var outcome = (string?)unitTestResult.Attribute("outcome") ?? "NotExecuted";
                var durationStr = (string?)unitTestResult.Attribute("duration") ?? "00:00:00";

                if (!testDefinitions.TryGetValue(testId, out var testInfo))
                    continue;

                var testResult = new NormalizedTestResult
                {
                    Identity = new FailureIdentity
                    {
                        TestName = testInfo.Name,
                        ClassName = testInfo.ClassName,
                        AssemblyName = "ISCM.Tests" // Extract from TRX if needed
                    },
                    Outcome = ParseOutcome(outcome),
                    Duration = ParseDuration(durationStr),
                    ExecutedAt = DateTime.UtcNow // Could parse from 'timestamp' attribute
                };

                // Extract Error Info if failed
                if (outcome == "Failed")
                {
                    var outputElement = unitTestResult.Element(ns + "Output");
                    var errorInfoElement = outputElement?.Element(ns + "ErrorInfo");

                    if (errorInfoElement != null)
                    {
                        testResult.ErrorMessage = (string?)errorInfoElement.Element(ns + "Message");
                        testResult.StackTrace = (string?)errorInfoElement.Element(ns + "StackTrace");
                    }
                }

                session.TestExecutions.Add(testResult);
            }
        }

        session.EndedAt = DateTime.UtcNow;
        return session;
    }

    private static TestOutcome ParseOutcome(string outcome)
    {
        return outcome switch
        {
            "Passed" => TestOutcome.Passed,
            "Failed" => TestOutcome.Failed,
            "NotExecuted" => TestOutcome.Skipped,
            _ => TestOutcome.Unknown
        };
    }

    private static TimeSpan ParseDuration(string duration)
    {
        // Format: hh:mm:ss.ff
        try
        {
            return TimeSpan.Parse(duration);
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }
}