using System.Text.Json;
using ISCM.Application.Interfaces;
using ISCM.Application.Reporting;
using Microsoft.Extensions.Logging;

namespace ISCM.Application.Services;

/// <summary>
/// File-based report template manager.
/// Stores custom templates as JSON files for Air-Gapped compatibility.
/// Includes built-in templates that cannot be deleted.
/// Phase 16.3 implementation.
/// </summary>
public class ReportTemplateService : IReportTemplateService
{
    private readonly IStoragePathProvider _pathProvider;
    private readonly ILogger<ReportTemplateService> _logger;
    private readonly string _templatesDirectory;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Built-in template IDs (cannot be deleted)
    private const string BuiltinExecutiveId = "builtin-executive";
    private const string BuiltinTechnicalId = "builtin-technical";
    private const string BuiltinComplianceId = "builtin-compliance";

    private static readonly HashSet<string> BuiltinIds = new(StringComparer.OrdinalIgnoreCase)
    {
        BuiltinExecutiveId, BuiltinTechnicalId, BuiltinComplianceId
    };

    public ReportTemplateService(IStoragePathProvider pathProvider, ILogger<ReportTemplateService> logger)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Store templates in Data/Templates/ next to the SQLite database
        var dbPath = _pathProvider.GetDatabasePath();
        var dataDir = Path.GetDirectoryName(dbPath) ?? AppContext.BaseDirectory;
        _templatesDirectory = Path.Combine(dataDir, "Templates");
        Directory.CreateDirectory(_templatesDirectory);
    }

    public async Task<IReadOnlyList<ReportTemplate>> ListTemplatesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<ReportTemplate>();

        // 1. Add built-in templates (always available)
        result.AddRange(GetBuiltInTemplates());

        // 2. Load custom templates from disk
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!Directory.Exists(_templatesDirectory))
                return result;

            foreach (var file in Directory.GetFiles(_templatesDirectory, "*.json"))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file, cancellationToken);
                    var template = JsonSerializer.Deserialize<ReportTemplate>(json, JsonOptions);
                    if (template != null && !BuiltinIds.Contains(template.TemplateId))
                    {
                        result.Add(template);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize template file: {File}", file);
                }
            }
        }
        finally
        {
            _lock.Release();
        }

        // Sort: built-in first, then custom alphabetically
        return result
            .OrderByDescending(t => BuiltinIds.Contains(t.TemplateId))
            .ThenBy(t => t.Name)
            .ToList();
    }

    public async Task<ReportTemplate?> GetTemplateAsync(string templateId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(templateId))
            return null;

        // Check built-in first
        var builtin = GetBuiltInTemplates().FirstOrDefault(t => t.TemplateId == templateId);
        if (builtin != null) return builtin;

        // Check disk
        var filePath = GetTemplatePath(templateId);
        if (!File.Exists(filePath)) return null;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            return JsonSerializer.Deserialize<ReportTemplate>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load template: {Id}", templateId);
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ReportTemplate> SaveTemplateAsync(ReportTemplate template, CancellationToken cancellationToken = default)
    {
        if (template == null) throw new ArgumentNullException(nameof(template));

        if (BuiltinIds.Contains(template.TemplateId))
            throw new InvalidOperationException($"Cannot overwrite built-in template: {template.TemplateId}");

        var filePath = GetTemplatePath(template.TemplateId);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var json = JsonSerializer.Serialize(template, JsonOptions);
            await File.WriteAllTextAsync(filePath, json, cancellationToken);
            _logger.LogInformation("Template saved: {Id} -> {Path}", template.TemplateId, filePath);
            return template;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DeleteTemplateAsync(string templateId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(templateId))
            return false;

        if (BuiltinIds.Contains(templateId))
        {
            _logger.LogWarning("Attempted to delete built-in template: {Id}", templateId);
            return false;
        }

        var filePath = GetTemplatePath(templateId);
        if (!File.Exists(filePath))
            return false;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            File.Delete(filePath);
            _logger.LogInformation("Template deleted: {Id}", templateId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete template: {Id}", templateId);
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    public ReportTemplate CreateFromOptions(
        string name,
        string description,
        ReportGenerationOptions options,
        string createdBy = "User")
    {
        var sections = new List<TemplateSectionConfig>
        {
            new() { Key = "executive", TitleEn = "Executive Summary", TitleFa = "خلاصه مدیریتی", Enabled = options.IncludeExecutiveSummary, Order = 1 },
            new() { Key = "findings", TitleEn = "Technical Findings", TitleFa = "یافته‌های فنی", Enabled = options.IncludeTechnicalFindings, Order = 2 },
            new() { Key = "risk", TitleEn = "Risk Assessment", TitleFa = "ارزیابی ریسک", Enabled = options.IncludeRiskAssessment, Order = 3 },
            new() { Key = "beforeafter", TitleEn = "Before/After Comparison", TitleFa = "مقایسه قبل/بعد", Enabled = options.IncludeBeforeAfterComparison, Order = 4 }
        };

        return new ReportTemplate
        {
            TemplateId = Guid.NewGuid().ToString("N"),
            Name = name,
            Description = description,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = createdBy,
            Sections = sections,
            PreferredFormat = "pdf",
            IncludeRawEvidence = options.IncludeRawEvidence,
            IncludeFileHashes = options.IncludeFileHashes,
            IsBilingual = options.IsBilingual
        };
    }

    private string GetTemplatePath(string templateId)
    {
        // Sanitize ID to prevent path traversal
        var safeId = new string(templateId.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrEmpty(safeId))
            safeId = Guid.NewGuid().ToString("N");
        return Path.Combine(_templatesDirectory, $"{safeId}.json");
    }

    private static IEnumerable<ReportTemplate> GetBuiltInTemplates()
    {
        yield return new ReportTemplate
        {
            TemplateId = BuiltinExecutiveId,
            Name = "Executive Summary",
            Description = "High-level overview for management (1-2 pages)",
            CreatedAtUtc = DateTime.UnixEpoch,
            CreatedBy = "System",
            PreferredFormat = "pdf",
            Sections = new List<TemplateSectionConfig>
            {
                new() { Key = "executive", TitleEn = "Executive Summary", TitleFa = "خلاصه مدیریتی", Enabled = true, Order = 1 },
                new() { Key = "risk", TitleEn = "Risk Assessment", TitleFa = "ارزیابی ریسک", Enabled = true, Order = 2 }
            }
        };

        yield return new ReportTemplate
        {
            TemplateId = BuiltinTechnicalId,
            Name = "Technical Audit",
            Description = "Detailed technical findings for engineers",
            CreatedAtUtc = DateTime.UnixEpoch,
            CreatedBy = "System",
            PreferredFormat = "excel",
            Sections = new List<TemplateSectionConfig>
            {
                new() { Key = "findings", TitleEn = "Technical Findings", TitleFa = "یافته‌های فنی", Enabled = true, Order = 1 },
                new() { Key = "executive", TitleEn = "Executive Summary", TitleFa = "خلاصه مدیریتی", Enabled = false, Order = 2 }
            }
        };

        yield return new ReportTemplate
        {
            TemplateId = BuiltinComplianceId,
            Name = "Full Compliance Package",
            Description = "Complete compliance with evidence and hashes",
            CreatedAtUtc = DateTime.UnixEpoch,
            CreatedBy = "System",
            PreferredFormat = "zip",
            IncludeRawEvidence = true,
            IncludeFileHashes = true,
            Sections = new List<TemplateSectionConfig>
            {
                new() { Key = "executive", TitleEn = "Executive Summary", TitleFa = "خلاصه مدیریتی", Enabled = true, Order = 1 },
                new() { Key = "findings", TitleEn = "Technical Findings", TitleFa = "یافته‌های فنی", Enabled = true, Order = 2 },
                new() { Key = "risk", TitleEn = "Risk Assessment", TitleFa = "ارزیابی ریسک", Enabled = true, Order = 3 },
                new() { Key = "beforeafter", TitleEn = "Before/After", TitleFa = "قبل/بعد", Enabled = true, Order = 4 }
            }
        };
    }
}