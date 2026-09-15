using ISCM.Application.Reporting;
using ISCM.Domain.Entities;

namespace ISCM.Application.Interfaces;

public interface IReportService
{
    // --- Existing Methods (Phase 14) ---
    Task<string> GenerateAndSaveReportAsync(ScanResult scanResult, string outputDir, string baseFileName);
    Task<string> GenerateAndSaveJsonReportAsync(ScanResult scanResult, string outputDir, string baseFileName);
    Task<string> GenerateAndSaveCsvReportAsync(ScanResult scanResult, string outputDir, string baseFileName);

    // --- NEW Methods (Phase 16.1) ---

    /// <summary>
    /// Generates a professional, printable PDF report using QuestPDF.
    /// </summary>
    Task<string> GeneratePdfReportAsync(ScanResult scanResult, string outputDir, string baseFileName, ReportGenerationOptions options);

    /// <summary>
    /// Generates a formatted .xlsx spreadsheet using ClosedXML.
    /// </summary>
    Task<string> GenerateExcelReportAsync(ScanResult scanResult, string outputDir, string baseFileName, ReportGenerationOptions options);

    /// <summary>
    /// Generates a ZIP bundle containing the report + optional raw evidence files.
    /// </summary>
    Task<string> GenerateZipBundleAsync(ScanResult scanResult, string outputDir, string baseFileName, ReportGenerationOptions options);
}