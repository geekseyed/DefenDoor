using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-04: Evidence Engine
/// Collects and packages evidence related to a detected failure.
/// </summary>
public class EvidenceService
{
    public EvidencePackage CollectEvidence(Failure failure, NormalizedTestResult? testResult = null)
    {
        var package = new EvidencePackage
        {
            FailureId = failure.Identity.ToFullString(),
            CollectedAt = DateTime.UtcNow,
            Items = new List<EvidenceItem>()
        };

        // 1. Test Execution Evidence
        if (testResult != null)
        {
            package.Items.Add(new EvidenceItem
            {
                Type = EvidenceType.TestExecution,
                Content = $"Test: {testResult.Identity.TestName}, Outcome: {testResult.Outcome}, Duration: {testResult.Duration}",
                Provenance = new EvidenceProvenance { Source = "TRX", Timestamp = testResult.ExecutedAt }
            });
        }

        // 2. Exception Evidence
        if (!string.IsNullOrEmpty(failure.StackTrace))
        {
            package.Items.Add(new EvidenceItem
            {
                Type = EvidenceType.Exception,
                Content = failure.StackTrace,
                Provenance = new EvidenceProvenance { Source = "StackTrace", Timestamp = failure.DetectedAt }
            });
        }

        // 3. Domain Evaluation Evidence
        if (failure.Metadata != null && failure.Metadata.ContainsKey("SubControlId"))
        {
            var expected = failure.Metadata.GetValueOrDefault("Expected", "N/A");
            var actual = failure.Metadata.GetValueOrDefault("Actual", "N/A");

            package.Items.Add(new EvidenceItem
            {
                Type = EvidenceType.DomainEvaluation,
                Content = $"SubControl: {failure.Metadata["SubControlId"]}, Expected: {expected}, Actual: {actual}",
                Provenance = new EvidenceProvenance { Source = "DomainEvaluator", Timestamp = failure.DetectedAt }
            });
        }

        // 4. Localization Evidence
        if (failure.Localization != null)
        {
            var locInfo = $"File: {failure.Localization.PrimaryFilePath ?? "Unknown"}, Line: {failure.Localization.PrimaryLineNumber?.ToString() ?? "Unknown"}, Method: {failure.Localization.MethodName ?? "Unknown"}";
            package.Items.Add(new EvidenceItem
            {
                Type = EvidenceType.SourceLocation,
                Content = locInfo,
                Provenance = new EvidenceProvenance { Source = "LocalizationService", Timestamp = failure.DetectedAt }
            });
        }

        // 5. Error Message Evidence
        if (!string.IsNullOrEmpty(failure.Message))
        {
            package.Items.Add(new EvidenceItem
            {
                Type = EvidenceType.ErrorMessage,
                Content = failure.Message,
                Provenance = new EvidenceProvenance { Source = "FailureDetection", Timestamp = failure.DetectedAt }
            });
        }

        return package;
    }
}