using System.Text.RegularExpressions;
using ISCM.BugFinder.Core.Models;
using ISCM.Domain.Enums;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// Parses console output from test runs to extract Domain Evaluation Results.
/// BF-01.3: Domain Evaluation Result Collection
/// </summary>
public class ConsoleEvaluationParser
{
    // Pattern matches lines like: "  EVL-001.4: Fail (Reason=Integer 0 is less than 32768.)"
    private static readonly Regex EvaluationPattern = new(
        @"^\s+(?<SubControlId>[A-Z0-9\-\.]+):\s+(?<Status>Pass|Fail|Error|Unknown|Disagreement)\s+\(Reason=(?<Reason>.+)\)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Parses a single line of console output. Returns null if the line is not an evaluation result.
    /// </summary>
    public NormalizedEvaluationResult? ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var match = EvaluationPattern.Match(line);
        if (!match.Success)
            return null;

        var subControlId = match.Groups["SubControlId"].Value;
        var statusText = match.Groups["Status"].Value;
        var reason = match.Groups["Reason"].Value;

        var status = statusText switch
        {
            "Pass" => CheckStatus.Pass,
            "Fail" => CheckStatus.Fail,
            "Error" => CheckStatus.Error,
            "Disagreement" => CheckStatus.Disagreement,
            _ => CheckStatus.Unknown
        };

        return new NormalizedEvaluationResult
        {
            SubControlId = subControlId,
            Status = status,
            Reason = reason
        };
    }

    /// <summary>
    /// Parses the entire console output and returns a list of evaluation results.
    /// </summary>
    public List<NormalizedEvaluationResult> Parse(string consoleOutput)
    {
        var results = new List<NormalizedEvaluationResult>();

        if (string.IsNullOrWhiteSpace(consoleOutput))
            return results;

        using (var reader = new StringReader(consoleOutput))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var result = ParseLine(line);
                if (result != null)
                {
                    results.Add(result);
                }
            }
        }

        return results;
    }
}