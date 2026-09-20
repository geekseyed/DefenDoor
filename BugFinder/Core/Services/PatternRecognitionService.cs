using ISCM.BugFinder.Core.Models;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-09.3: Pattern Recognition Service
/// Identifies recurring failure signatures and groups them into patterns.
/// </summary>
public class PatternRecognitionService
{
    /// <summary>
    /// Analyzes a list of failures to find recurring patterns based on error message similarity.
    /// </summary>
    public List<FailurePattern> IdentifyPatterns(List<Failure> failures)
    {
        if (failures == null || failures.Count == 0) return new List<FailurePattern>();

        // Group by normalized error signature
        var groups = failures
            .Where(f => !string.IsNullOrEmpty(f.Message))
            .GroupBy(f => GenerateSignature(f.Message))
            .Where(g => g.Count() >= 2) // Only patterns with 2+ occurrences
            .Select(g => new FailurePattern
            {
                Signature = g.Key,
                RelatedTestIds = g.Select(f => f.Identity.ToFullString()).Distinct().ToList(),
                OccurrenceCount = g.Count(),
                FirstSeen = g.Min(f => f.DetectedAt),
                LastSeen = g.Max(f => f.DetectedAt),
                SuggestedFix = GenerateSuggestion(g.First())
            })
            .OrderByDescending(p => p.OccurrenceCount)
            .ToList();

        return groups;
    }

    /// <summary>
    /// Generates a stable hash/signature from an error message, ignoring variable parts (like timestamps or specific values).
    /// </summary>
    private string GenerateSignature(string message)
    {
        // Simple normalization: remove numbers, whitespace, and lowercase
        // In a real scenario, use Regex to remove specific dynamic values (GUIDs, Dates, Paths)
        var normalized = System.Text.RegularExpressions.Regex.Replace(message.ToLower(), @"\d+", "N");

        using (var sha256 = SHA256.Create())
        {
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalized));
            return Convert.ToBase64String(bytes);
        }
    }

    private string? GenerateSuggestion(Failure failure)
    {
        if (failure.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return "Consider increasing the timeout limit or optimizing the slow operation.";

        if (failure.Message.Contains("null", StringComparison.OrdinalIgnoreCase))
            return "Add null checks before accessing object properties.";

        if (failure.Message.Contains("connection", StringComparison.OrdinalIgnoreCase))
            return "Verify network stability and connection string configuration.";

        return null;
    }
}