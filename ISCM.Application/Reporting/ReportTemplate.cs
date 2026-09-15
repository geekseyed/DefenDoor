namespace ISCM.Application.Reporting;

/// <summary>
/// Represents a saved, reusable report configuration template.
/// Phase 16.3: Custom Report Templates.
/// </summary>
public sealed class ReportTemplate
{
    public string TemplateId { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? ModifiedAtUtc { get; init; }
    public string CreatedBy { get; init; } = "System";

    // ── Section Configuration ──
    public IReadOnlyList<TemplateSectionConfig> Sections { get; init; } = Array.Empty<TemplateSectionConfig>();

    // ── Format & Output Preferences ──
    public string PreferredFormat { get; init; } = "pdf";
    public bool IncludeRawEvidence { get; init; } = false;
    public bool IncludeFileHashes { get; init; } = false;
    public bool IsBilingual { get; init; } = false;

    // ── Branding ──
    public string? CompanyName { get; init; }
    public string? ReportTitle { get; init; }
    public string? FooterNote { get; init; }

    // ── Filters ──
    public bool IncludePassedFindings { get; init; } = true;
    public bool IncludeSuppressedFindings { get; init; } = false;

    /// <summary>
    /// Converts this template to ReportGenerationOptions for engine consumption.
    /// </summary>
    public ReportGenerationOptions ToGenerationOptions()
    {
        return new ReportGenerationOptions
        {
            IncludeExecutiveSummary = Sections.Any(s => s.Key == "executive" && s.Enabled),
            IncludeTechnicalFindings = Sections.Any(s => s.Key == "findings" && s.Enabled),
            IncludeRiskAssessment = Sections.Any(s => s.Key == "risk" && s.Enabled),
            IncludeBeforeAfterComparison = Sections.Any(s => s.Key == "beforeafter" && s.Enabled),
            IncludeRawEvidence = IncludeRawEvidence,
            IncludeFileHashes = IncludeFileHashes,
            IsBilingual = IsBilingual,
            EncryptOutput = false
        };
    }
}