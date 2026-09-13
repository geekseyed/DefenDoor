using ISCM.Application.Interfaces;
using ISCM.Application.Parsers;
using ISCM.Domain.Entities;
using ISCM.Domain.Enums;
using ISCM.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace ISCM.Infrastructure.Scanning.Checks;

/// <summary>
/// Phase 10.7: Advanced Audit Policy Check (Collector-only pattern).
/// Phase 14.3: Refactored to use IProcessCacheService for command execution.
///
/// این چک **فقط Collector** است:
/// - Evidence تولید می‌کند (RawOutput + TypedValue)
/// - Evidence.Evaluation = NotScanned (ارزیابی نمی‌کند)
/// - Scanner مسئول ارزیابی تایپ‌شده با استفاده از کاتالوگ است
///
/// 11 SubControls از نوع Collection با SetMembership operator.
/// </summary>
[SupportedOSPlatform("windows")]
public class AdvancedAuditCheck : BaseHardeningCheck
{
    private readonly IEvidenceParser _registryParser;
    private readonly IProcessCacheService _processCache;

    public override string CheckId => "AUD-001";
    public override string Name => "Advanced Audit Policy";
    public override CheckCategory Category => CheckCategory.Audit;
    public override CheckSeverity Severity => CheckSeverity.Medium;

    // Mapping: SubControlId → auditpol subcategory name
    private static readonly Dictionary<string, string> SubControlToSubcategory = new()
    {
        { "AUD-001.1", "Logon" },
        { "AUD-001.2", "Logoff" },
        { "AUD-001.3", "Special Logon" },
        { "AUD-001.4", "Credential Validation" },
        { "AUD-001.5", "User Account Management" },
        { "AUD-001.6", "Security Group Management" },
        { "AUD-001.7", "Process Creation" },
        { "AUD-001.8", "Authentication Policy Change" },
        { "AUD-001.9", "Authorization Policy Change" },
        { "AUD-001.10", "Security State Change" },
        { "AUD-001.11", "Security System Extension" }
    };

    public AdvancedAuditCheck(IProcessCacheService processCache)
    {
        _registryParser = new RegistryParser();
        _processCache = processCache ?? throw new ArgumentNullException(nameof(processCache));
    }

    public override async Task<List<Evidence>> CollectEvidenceAsync()
    {
        var evidenceList = new List<Evidence>();

        // Run auditpol once for all subcategories (cached)
        var processResult = await _processCache.GetOrRunAsync("auditpol", "/get /category:*");
        var allOutput = processResult.OutputOrError;

        foreach (var kvp in SubControlToSubcategory)
        {
            var subControlId = kvp.Key;
            var subcategory = kvp.Value;

            try
            {
                var startTime = DateTime.UtcNow;
                var parsedValue = _registryParser.Parse(allOutput, "AuditPol");
                var typedValue = ExtractAuditSettingsFromOutput(allOutput, subcategory);

                evidenceList.Add(new Evidence
                {
                    EvidenceId = $"{CheckId}-{subControlId}",
                    SubControlId = subControlId,
                    SourceType = EvidenceSourceType.Other,  // ← تغییر از AuditPol
                    SourceName = $"auditpol /get /subcategory:\"{subcategory}\"",
                    AcquisitionCommand = $"auditpol /get /subcategory:\"{subcategory}\"",
                    RawOutput = ExtractSubcategoryBlock(allOutput, subcategory),
                    ParsedValue = parsedValue,
                    TypedValue = typedValue,
                    Evaluation = CheckStatus.NotScanned,
                    CollectedAtUtc = DateTime.UtcNow,
                    CollectionDurationMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds
                });
            }
            catch (Exception ex)
            {
                evidenceList.Add(CreateErrorEvidence(subControlId, ex));
            }
        }

        return evidenceList;
    }

    /// <summary>
    /// Extracts the block of text relevant to a specific subcategory.
    /// </summary>
    private static string ExtractSubcategoryBlock(string fullOutput, string subcategory)
    {
        if (string.IsNullOrWhiteSpace(fullOutput))
            return string.Empty;

        var lines = fullOutput.Split('\n');
        var result = new System.Text.StringBuilder();
        bool capture = false;

        foreach (var line in lines)
        {
            if (line.Contains(subcategory, StringComparison.OrdinalIgnoreCase))
            {
                capture = true;
                result.AppendLine(line);
                continue;
            }

            if (capture)
            {
                // Stop capturing when we hit the next subcategory (starts with spaces but new section)
                if (string.IsNullOrWhiteSpace(line))
                {
                    capture = false;
                    break;
                }
                result.AppendLine(line);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Parses auditpol output for a specific subcategory into a Collection of enabled settings.
    ///
    /// Possible auditpol outputs:
    ///   "Success and Failure" → ["Success", "Failure"]
    ///   "Success" → ["Success"]
    ///   "Failure" → ["Failure"]
    ///   "No Auditing" → []
    /// </summary>
    private static EvidenceValue ExtractAuditSettingsFromOutput(string fullOutput, string subcategory)
    {
        if (string.IsNullOrWhiteSpace(fullOutput))
            return new EvidenceValue(new List<object>(), EvidenceValueType.Collection, rawString: "(empty)");

        var block = ExtractSubcategoryBlock(fullOutput, subcategory);
        var enabledFlags = new List<object>();

        if (block.Contains("Success", StringComparison.OrdinalIgnoreCase) &&
            !block.Contains("No Auditing", StringComparison.OrdinalIgnoreCase))
        {
            enabledFlags.Add("Success");
        }

        if (block.Contains("Failure", StringComparison.OrdinalIgnoreCase) &&
            !block.Contains("No Auditing", StringComparison.OrdinalIgnoreCase))
        {
            enabledFlags.Add("Failure");
        }

        return new EvidenceValue(
            enabledFlags,
            EvidenceValueType.Collection,
            rawString: enabledFlags.Count > 0 ? string.Join(", ", enabledFlags) : "(none)");
    }

    private static Evidence CreateErrorEvidence(string subControlId, Exception ex)
    {
        return new Evidence
        {
            EvidenceId = $"AUD-001-{subControlId}",
            SubControlId = subControlId,
            SourceType = EvidenceSourceType.Other,  // ← تغییر از AuditPol
            SourceName = "auditpol",
            RawOutput = ex.Message,
            TypedValue = null,
            Evaluation = CheckStatus.Error,
            Error = ex.Message,
            CollectedAtUtc = DateTime.UtcNow
        };
    }
}