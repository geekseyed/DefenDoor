namespace ISCM.Application.Reporting;

/// <summary>
/// Configuration options for advanced report generation (Phase 16.1).
/// Allows granular control over what sections are included in the output.
/// </summary>
public class ReportGenerationOptions
{
    // Core Sections
    public bool IncludeExecutiveSummary { get; set; } = true;
    public bool IncludeTechnicalFindings { get; set; } = true;
    public bool IncludeRiskAssessment { get; set; } = true;

    // Advanced Sections
    public bool IncludeBeforeAfterComparison { get; set; } = false;
    public bool IncludeRawEvidence { get; set; } = false;
    public bool IncludeFileHashes { get; set; } = false;

    // Formatting
    public bool IsBilingual { get; set; } = false; // EN + FA
    public bool EncryptOutput { get; set; } = false; // Future: Password protection
}