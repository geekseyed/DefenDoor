using ISCM.Application.Reporting;

namespace ISCM.Application.Interfaces;

/// <summary>
/// Service for managing custom report templates.
/// Templates are persisted as JSON files for Air-Gapped compatibility.
/// </summary>
public interface IReportTemplateService
{
    /// <summary>Lists all available templates (built-in + custom).</summary>
    Task<IReadOnlyList<ReportTemplate>> ListTemplatesAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets a template by ID.</summary>
    Task<ReportTemplate?> GetTemplateAsync(string templateId, CancellationToken cancellationToken = default);

    /// <summary>Saves a new or updated custom template.</summary>
    Task<ReportTemplate> SaveTemplateAsync(ReportTemplate template, CancellationToken cancellationToken = default);

    /// <summary>Deletes a custom template. Built-in templates cannot be deleted.</summary>
    Task<bool> DeleteTemplateAsync(string templateId, CancellationToken cancellationToken = default);

    /// <summary>Creates a template from the current UI state.</summary>
    ReportTemplate CreateFromOptions(string name, string description, ReportGenerationOptions options, string createdBy = "User");
}