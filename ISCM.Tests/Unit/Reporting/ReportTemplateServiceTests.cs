using ISCM.Application.Interfaces;
using ISCM.Application.Reporting;
using ISCM.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ISCM.Tests.Unit.Reporting;

/// <summary>
/// Unit tests for ReportTemplateService (Phase 16.3 - Custom Report Templates).
/// Uses a FakeStoragePathProvider to isolate file system operations in a temp directory.
/// </summary>
public class ReportTemplateServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ReportTemplateService _service;

    public ReportTemplateServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ISCM_TemplateTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var fakeDbPath = Path.Combine(_tempDir, "defendoor.db");
        var pathProvider = new FakeStoragePathProvider(fakeDbPath);
        var logger = NullLogger<ReportTemplateService>.Instance;

        _service = new ReportTemplateService(pathProvider, logger);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { /* Ignore cleanup errors */ }
        }
    }

    [Fact]
    public async Task ListTemplatesAsync_AlwaysReturnsBuiltInTemplates()
    {
        // Act
        var templates = await _service.ListTemplatesAsync();

        // Assert
        Assert.Contains(templates, t => t.Name == "Executive Summary");
        Assert.Contains(templates, t => t.Name == "Technical Audit");
        Assert.Contains(templates, t => t.Name == "Full Compliance Package");
    }

    [Fact]
    public async Task SaveAndGetTemplateAsync_PersistsAndRetrievesCustomTemplate()
    {
        // Arrange
        var options = new ReportGenerationOptions { IncludeExecutiveSummary = true };
        var template = _service.CreateFromOptions("My Custom Audit", "Test description", options);

        // Act
        await _service.SaveTemplateAsync(template);
        var loaded = await _service.GetTemplateAsync(template.TemplateId);

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal("My Custom Audit", loaded!.Name);
        Assert.Equal("Test description", loaded.Description);
    }

    [Fact]
    public async Task DeleteTemplateAsync_RemovesCustomTemplateFromDisk()
    {
        // Arrange
        var template = _service.CreateFromOptions("To Delete", "desc", new ReportGenerationOptions());
        await _service.SaveTemplateAsync(template);

        // Act
        var deleted = await _service.DeleteTemplateAsync(template.TemplateId);
        var loaded = await _service.GetTemplateAsync(template.TemplateId);

        // Assert
        Assert.True(deleted);
        Assert.Null(loaded);
    }

    [Fact]
    public async Task DeleteTemplateAsync_PreventsDeletingBuiltInTemplates()
    {
        // Arrange
        var templates = await _service.ListTemplatesAsync();
        var builtin = templates.First(t => t.Name == "Executive Summary");

        // Act
        var deleted = await _service.DeleteTemplateAsync(builtin.TemplateId);
        var loaded = await _service.GetTemplateAsync(builtin.TemplateId);

        // Assert
        Assert.False(deleted);
        Assert.NotNull(loaded); // Still exists
    }

    [Fact]
    public async Task SaveTemplateAsync_PreventsOverwritingBuiltInTemplates()
    {
        // Arrange
        var templates = await _service.ListTemplatesAsync();
        var builtin = templates.First(t => t.Name == "Executive Summary");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveTemplateAsync(builtin));
    }

    [Fact]
    public void CreateFromOptions_MapsOptionsToTemplateSectionsCorrectly()
    {
        // Arrange
        var options = new ReportGenerationOptions
        {
            IncludeExecutiveSummary = true,
            IncludeTechnicalFindings = false,
            IncludeRiskAssessment = true,
            IncludeRawEvidence = true,
            IsBilingual = true
        };

        // Act
        var template = _service.CreateFromOptions("Test Template", "desc", options);

        // Assert
        Assert.Equal("Test Template", template.Name);
        Assert.True(template.IncludeRawEvidence);
        Assert.True(template.IsBilingual);

        var execSection = template.Sections.First(s => s.Key == "executive");
        Assert.True(execSection.Enabled);

        var findingsSection = template.Sections.First(s => s.Key == "findings");
        Assert.False(findingsSection.Enabled);
    }

    [Fact]
    public async Task ListTemplatesAsync_SortsBuiltInFirstThenCustom()
    {
        // Arrange
        var custom1 = _service.CreateFromOptions("Zebra Custom", "desc", new ReportGenerationOptions());
        var custom2 = _service.CreateFromOptions("Alpha Custom", "desc", new ReportGenerationOptions());
        await _service.SaveTemplateAsync(custom1);
        await _service.SaveTemplateAsync(custom2);

        // Act
        var templates = await _service.ListTemplatesAsync();

        // Assert - Built-in templates should come first
        Assert.StartsWith("builtin-", templates[0].TemplateId);

        // Custom templates should be sorted alphabetically
        var customTemplates = templates.Where(t => !t.TemplateId.StartsWith("builtin-")).ToList();
        Assert.Equal("Alpha Custom", customTemplates[0].Name);
        Assert.Equal("Zebra Custom", customTemplates[1].Name);
    }

    // ── Fake Storage Path Provider ──
    // ── Fake Storage Path Provider ──
    private sealed class FakeStoragePathProvider : IStoragePathProvider
    {
        private readonly string _rootDir;
        private readonly string _dbPath;

        public FakeStoragePathProvider(string dbPath)
        {
            _dbPath = dbPath;
            _rootDir = Path.GetDirectoryName(dbPath) ?? Path.GetTempPath();
            Directory.CreateDirectory(_rootDir);
        }

        public string GetRootPath() => _rootDir;
        public string GetDatabasePath() => _dbPath;
        public string GetMigrationsPath() => Path.Combine(_rootDir, "Migrations");
        public string GetTempPath() => Path.Combine(_rootDir, "Temp");
        public string GetReportsPath() => Path.Combine(_rootDir, "Reports");
        public string GetBackupsPath() => Path.Combine(_rootDir, "Backups");

        public bool IsWritable() => true;

        public StoragePathInfo GetStorageInfo() => new()
        {
            RootPath = _rootDir,
            DatabasePath = _dbPath,
            RootExists = Directory.Exists(_rootDir),
            DatabaseExists = File.Exists(_dbPath),
            IsWritable = true,
            AvailableBytes = null,
            ConfigurationSource = "Fake (Test)"
        };
    }
}