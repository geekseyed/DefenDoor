using System.IO.Compression;
using ClosedXML.Excel;
using ISCM.Application.Reporting;
using ISCM.Domain.Entities;
using ISCM.Domain.Enums;
using ISCM.Infrastructure.Reporting;
using Xunit;

namespace ISCM.Tests.Unit.Reporting;

/// <summary>
/// Unit tests for HtmlReportGenerator (Phase 16.1 - Multi-Format Report Engine).
/// Validates PDF, Excel, and ZIP Bundle generation capabilities.
/// </summary>
public class HtmlReportGeneratorTests : IDisposable
{
    private readonly HtmlReportGenerator _generator;
    private readonly string _testOutputDir;
    private readonly ScanResult _sampleScanResult;

    public HtmlReportGeneratorTests()
    {
        _generator = new HtmlReportGenerator();
        _testOutputDir = Path.Combine(Path.GetTempPath(), $"ISCM_Reporting_Tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testOutputDir);
        _sampleScanResult = CreateSampleScanResult();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testOutputDir))
        {
            try { Directory.Delete(_testOutputDir, true); }
            catch { /* Ignore cleanup errors in tests */ }
        }
    }

    // ── PDF Tests ──

    [Fact]
    public async Task GeneratePdfReportAsync_ValidScan_CreatesPdfFile()
    {
        // Arrange
        var options = new ReportGenerationOptions();
        var baseFileName = "TestReport";

        // Act
        var filePath = await _generator.GeneratePdfReportAsync(_sampleScanResult, _testOutputDir, baseFileName, options);

        // Assert
        Assert.NotNull(filePath);
        Assert.True(File.Exists(filePath), $"PDF file should exist at {filePath}");
        Assert.EndsWith(".pdf", filePath, StringComparison.OrdinalIgnoreCase);
        Assert.True(new FileInfo(filePath).Length > 0, "PDF file should not be empty");
    }

    [Fact]
    public async Task GeneratePdfReportAsync_WithDefaultOptions_ReturnsValidPath()
    {
        // Arrange
        var options = new ReportGenerationOptions
        {
            IncludeExecutiveSummary = true,
            IncludeTechnicalFindings = true
        };

        // Act
        var filePath = await _generator.GeneratePdfReportAsync(_sampleScanResult, _testOutputDir, "PdfWithSections", options);

        // Assert
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public async Task GeneratePdfReportAsync_WithAllSectionsDisabled_StillGeneratesFile()
    {
        // Arrange
        var options = new ReportGenerationOptions
        {
            IncludeExecutiveSummary = false,
            IncludeTechnicalFindings = false,
            IncludeRiskAssessment = false
        };

        // Act
        var filePath = await _generator.GeneratePdfReportAsync(_sampleScanResult, _testOutputDir, "PdfMinimal", options);

        // Assert
        Assert.True(File.Exists(filePath));
    }

    // ── Excel Tests ──

    [Fact]
    public async Task GenerateExcelReportAsync_ValidScan_CreatesXlsxFile()
    {
        // Arrange
        var options = new ReportGenerationOptions();
        var baseFileName = "TestExcel";

        // Act
        var filePath = await _generator.GenerateExcelReportAsync(_sampleScanResult, _testOutputDir, baseFileName, options);

        // Assert
        Assert.NotNull(filePath);
        Assert.True(File.Exists(filePath), $"Excel file should exist at {filePath}");
        Assert.EndsWith(".xlsx", filePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateExcelReportAsync_ValidScan_ContainsExpectedHeaders()
    {
        // Arrange
        var options = new ReportGenerationOptions();
        var filePath = await _generator.GenerateExcelReportAsync(_sampleScanResult, _testOutputDir, "ExcelHeaders", options);

        // Act & Assert - Verify workbook structure using ClosedXML
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets.First();

        Assert.Equal("Check ID", worksheet.Cell(1, 1).GetString());
        Assert.Equal("Name", worksheet.Cell(1, 2).GetString());
        Assert.Equal("Severity", worksheet.Cell(1, 3).GetString());
        Assert.Equal("Status", worksheet.Cell(1, 4).GetString());
        Assert.Equal("Current Value", worksheet.Cell(1, 5).GetString());
        Assert.Equal("Expected Value", worksheet.Cell(1, 6).GetString());
        Assert.Equal("Risk Score", worksheet.Cell(1, 7).GetString());
    }

    [Fact]
    public async Task GenerateExcelReportAsync_ValidScan_ContainsAllFindings()
    {
        // Arrange
        var options = new ReportGenerationOptions();
        var filePath = await _generator.GenerateExcelReportAsync(_sampleScanResult, _testOutputDir, "ExcelData", options);

        // Act & Assert
        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets.First();

        // Row 1 is header, so data starts at row 2
        var expectedRows = _sampleScanResult.Findings.Count + 1;
        Assert.Equal(expectedRows, worksheet.RowsUsed().Count());
    }

    // ── ZIP Bundle Tests ──

    [Fact]
    public async Task GenerateZipBundleAsync_ValidScan_CreatesZipFile()
    {
        // Arrange
        var options = new ReportGenerationOptions();
        var baseFileName = "TestBundle";

        // Act
        var filePath = await _generator.GenerateZipBundleAsync(_sampleScanResult, _testOutputDir, baseFileName, options);

        // Assert
        Assert.NotNull(filePath);
        Assert.True(File.Exists(filePath), $"ZIP file should exist at {filePath}");
        Assert.EndsWith(".zip", filePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateZipBundleAsync_ValidScan_ContainsHtmlAndJsonReports()
    {
        // Arrange
        var options = new ReportGenerationOptions { IncludeRawEvidence = false };
        var filePath = await _generator.GenerateZipBundleAsync(_sampleScanResult, _testOutputDir, "BundleContent", options);

        // Act & Assert - Verify ZIP contains expected files
        using var archive = ZipFile.OpenRead(filePath);
        var entryNames = archive.Entries.Select(e => e.Name).ToList();

        Assert.Contains("Report.html", entryNames);
        Assert.Contains("Report.json", entryNames);
    }

    [Fact]
    public async Task GenerateZipBundleAsync_WithRawEvidence_IncludesEvidenceFile()
    {
        // Arrange
        var options = new ReportGenerationOptions { IncludeRawEvidence = true };
        var filePath = await _generator.GenerateZipBundleAsync(_sampleScanResult, _testOutputDir, "BundleWithEvidence", options);

        // Act & Assert
        using var archive = ZipFile.OpenRead(filePath);
        var entryNames = archive.Entries.Select(e => e.Name).ToList();

        Assert.Contains("RawEvidence.json", entryNames);
    }

    // ── Existing Methods Regression Tests ──

    [Fact]
    public async Task GenerateAndSaveReportAsync_ValidScan_CreatesHtmlFile()
    {
        // Act
        var filePath = await _generator.GenerateAndSaveReportAsync(_sampleScanResult, _testOutputDir, "RegressionHtml");

        // Assert
        Assert.NotNull(filePath);
        Assert.True(File.Exists(filePath));
        Assert.EndsWith(".html", filePath, StringComparison.OrdinalIgnoreCase);

        var content = await File.ReadAllTextAsync(filePath);
        Assert.Contains("DefenDoor Hardening Report", content);
        Assert.Contains(_sampleScanResult.Hostname, content);
    }

    [Fact]
    public async Task GenerateAndSaveJsonReportAsync_ValidScan_CreatesValidJson()
    {
        // Act
        var filePath = await _generator.GenerateAndSaveJsonReportAsync(_sampleScanResult, _testOutputDir, "RegressionJson");

        // Assert
        Assert.True(File.Exists(filePath));
        var content = await File.ReadAllTextAsync(filePath);
        Assert.False(string.IsNullOrWhiteSpace(content));

        // Verify it's valid JSON
        var jsonDoc = System.Text.Json.JsonDocument.Parse(content);
        Assert.Equal("DefenDoor Hardening Report", jsonDoc.RootElement.GetProperty("reportType").GetString());
    }

    [Fact]
    public async Task GenerateAndSaveCsvReportAsync_ValidScan_CreatesCsvFile()
    {
        // Act
        var filePath = await _generator.GenerateAndSaveCsvReportAsync(_sampleScanResult, _testOutputDir, "RegressionCsv");

        // Assert
        Assert.True(File.Exists(filePath));
        var lines = await File.ReadAllLinesAsync(filePath);
        Assert.Equal(_sampleScanResult.Findings.Count + 1, lines.Length); // +1 for header
        Assert.StartsWith("CheckId,Name", lines[0]);
    }

    // ── ReportGenerationOptions Tests ──

    [Fact]
    public void ReportGenerationOptions_DefaultValues_AreSensible()
    {
        // Arrange & Act
        var options = new ReportGenerationOptions();

        // Assert
        Assert.True(options.IncludeExecutiveSummary);
        Assert.True(options.IncludeTechnicalFindings);
        Assert.True(options.IncludeRiskAssessment);
        Assert.False(options.IncludeBeforeAfterComparison);
        Assert.False(options.IncludeRawEvidence);
        Assert.False(options.IncludeFileHashes);
        Assert.False(options.IsBilingual);
        Assert.False(options.EncryptOutput);
    }

    [Fact]
    public async Task GeneratePdfReportAsync_OptionsAreRespected_DoesNotThrow()
    {
        // Arrange
        var options = new ReportGenerationOptions
        {
            IncludeExecutiveSummary = true,
            IncludeTechnicalFindings = false,
            IncludeRiskAssessment = false,
            IncludeBeforeAfterComparison = false,
            IncludeRawEvidence = false
        };

        // Act & Assert - Should not throw
        var filePath = await _generator.GeneratePdfReportAsync(_sampleScanResult, _testOutputDir, "OptionsTest", options);
        Assert.True(File.Exists(filePath));
    }

    // ── Helper Methods ──

    private static ScanResult CreateSampleScanResult()
    {
        var scanResult = new ScanResult(
            hostname: "TEST-SERVER-01",
            ipAddress: "192.168.1.100",
            macAddress: "00:1A:2B:3C:4D:5E",
            osVersion: "Windows Server 2022",
            osBuild: "20348",
            mode: ScanMode.Full
        );

        // Add sample findings
        var finding1 = new Finding(
            checkId: "SEC-001",
            name: "Password Policy - Minimum Length",
            category: CheckCategory.Account,
            severity: CheckSeverity.High,
            status: CheckStatus.Pass,
            currentValue: "14",
            expectedValue: ">=14",
            recommendation: "Password length is compliant.",
            cisReference: "CIS 1.1.1",
            riskScore: 85
        );

        var finding2 = new Finding(
            checkId: "SEC-002",
            name: "SMBv1 Protocol Disabled",
            category: CheckCategory.System,
            severity: CheckSeverity.Critical,
            status: CheckStatus.Fail,
            currentValue: "Enabled",
            expectedValue: "Disabled",
            recommendation: "Disable SMBv1 protocol to prevent EternalBlue attacks.",
            cisReference: "CIS 18.3.1",
            riskScore: 95
        );

        scanResult.AddFinding(finding1);
        scanResult.AddFinding(finding2);
        scanResult.CompleteScan();

        return scanResult;
    }
}