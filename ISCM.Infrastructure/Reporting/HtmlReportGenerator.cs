using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using ISCM.Application.Interfaces;
using ISCM.Application.Reporting;
using ISCM.Domain.Entities;
using ISCM.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ISCM.Infrastructure.Reporting;

public class HtmlReportGenerator : IReportService
{
    // Initialize QuestPDF license (Community license for this project)
    static HtmlReportGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    // ── Existing HTML Method (Preserved from Phase 14) ──
    public async Task<string> GenerateAndSaveReportAsync(ScanResult scanResult, string outputDir, string baseFileName)
    {
        Directory.CreateDirectory(outputDir);
        var filePath = Path.Combine(outputDir, $"{baseFileName}.html");
        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"UTF-8\">");
        sb.AppendLine($"<title>DefenDoor Report - {Escape(scanResult.Hostname)}</title>");
        sb.AppendLine(@"<style>
body{font-family:'Segoe UI',Tahoma,sans-serif;background:#0f172a;color:#e2e8f0;padding:20px;margin:0;}
h1{color:#06b6d4;border-bottom:2px solid #06b6d4;padding-bottom:10px;}
h2{color:#94a3b8;margin-top:30px;}
.summary{background:#1e293b;padding:15px;border-radius:8px;margin-bottom:20px;}
.summary-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:15px;}
.summary-item{background:#0f172a;padding:12px;border-radius:6px;border-left:3px solid #06b6d4;}
.label{color:#64748b;font-size:11px;text-transform:uppercase;margin-bottom:5px;}
.value{color:#e2e8f0;font-size:16px;font-weight:bold;font-family:'Consolas',monospace;}
table{width:100%;border-collapse:collapse;margin-top:15px;}
th{background:#1e293b;color:#94a3b8;text-transform:uppercase;font-size:11px;letter-spacing:1px;padding:10px;text-align:left;border-bottom:2px solid #334155;}
td{padding:10px;border-bottom:1px solid #334155;}
.pass{color:#10b981;} .fail{color:#ef4444;} .warn{color:#f59e0b;}
.badge{display:inline-block;padding:3px 8px;border-radius:4px;font-size:10px;font-weight:bold;text-transform:uppercase;}
.badge-critical{background:rgba(239,68,68,0.2);color:#ef4444;}
.badge-high{background:rgba(245,158,11,0.2);color:#f59e0b;}
.badge-medium{background:rgba(226,232,240,0.1);color:#e2e8f0;}
.badge-low{background:rgba(148,163,184,0.2);color:#94a3b8;}
</style></head><body>");

        sb.AppendLine("<h1>DefenDoor Hardening Report</h1>");
        sb.AppendLine("<div class=\"summary\"><div class=\"summary-grid\">");
        sb.AppendLine(SummaryItem("Hostname", scanResult.Hostname));
        sb.AppendLine(SummaryItem("IP Address", scanResult.IpAddress));
        sb.AppendLine(SummaryItem("OS Version", $"{scanResult.OsVersion} (Build {scanResult.OsBuild})"));
        sb.AppendLine(SummaryItem("Scan Time", scanResult.StartedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")));
        var scoreColor = scanResult.ComplianceScore >= 70 ? "#10b981" : "#ef4444";
        sb.AppendLine($"<div class=\"summary-item\"><div class=\"label\">Compliance Score</div><div class=\"value\" style=\"color:{scoreColor}\">{scanResult.ComplianceScore}%</div></div>");
        sb.AppendLine(SummaryItem("Grade", scanResult.Grade));
        sb.AppendLine("</div></div>");
        sb.AppendLine("<h2>Scan Summary</h2>");
        sb.AppendLine($"<p><strong>Total Checks:</strong> {scanResult.Findings.Count} | ");
        sb.AppendLine($"<span class=\"pass\">Pass: {scanResult.PassCount}</span> | ");
        sb.AppendLine($"<span class=\"fail\">Fail: {scanResult.FailCount}</span> | ");
        sb.AppendLine($"<span class=\"warn\">Warning: {scanResult.WarningCount}</span></p>");
        sb.AppendLine("<h2>Findings</h2>");
        sb.AppendLine("<table><thead><tr>");
        sb.AppendLine("<th>ID</th><th>Name</th><th>Category</th><th>Severity</th><th>Status</th><th>Current</th><th>Expected</th><th>CIS Ref</th><th>Risk</th>");
        sb.AppendLine("</tr></thead><tbody>");
        foreach (var f in scanResult.Findings)
        {
            var statusClass = f.Status.ToString().ToLowerInvariant();
            var sevClass = $"badge-{f.Severity.ToString().ToLowerInvariant()}";
            sb.AppendLine("<tr>");
            sb.AppendLine($"<td><code style=\"color:#06b6d4\">{Escape(f.CheckId)}</code></td>");
            sb.AppendLine($"<td>{Escape(f.Name)}</td>");
            sb.AppendLine($"<td>{Escape(f.Category.ToString())}</td>");
            sb.AppendLine($"<td><span class=\"badge {sevClass}\">{Escape(f.Severity.ToString())}</span></td>");
            sb.AppendLine($"<td class=\"{statusClass}\">{Escape(f.Status.ToString())}</td>");
            sb.AppendLine($"<td>{Escape(f.CurrentValue)}</td>");
            sb.AppendLine($"<td>{Escape(f.ExpectedValue)}</td>");
            sb.AppendLine($"<td>{Escape(f.CisReference)}</td>");
            sb.AppendLine($"<td>{f.RiskScore}</td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody></table></body></html>");
        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
        return filePath;
    }

    public async Task<string> GenerateAndSaveJsonReportAsync(ScanResult scanResult, string outputDir, string baseFileName)
    {
        Directory.CreateDirectory(outputDir);
        var filePath = Path.Combine(outputDir, $"{baseFileName}.json");
        var findings = scanResult.Findings.Select(f => new FindingDto
        {
            CheckId = f.CheckId,
            Name = f.Name,
            Category = f.Category.ToString(),
            Severity = f.Severity.ToString(),
            Status = f.Status.ToString(),
            CurrentValue = f.CurrentValue,
            ExpectedValue = f.ExpectedValue,
            CisReference = f.CisReference,
            RiskScore = f.RiskScore,
            Recommendation = f.Recommendation
        }).ToList();
        var report = new ReportDto
        {
            ReportType = "DefenDoor Hardening Report",
            GeneratedAt = DateTime.UtcNow,
            Hostname = scanResult.Hostname,
            IpAddress = scanResult.IpAddress,
            OsVersion = scanResult.OsVersion,
            OsBuild = scanResult.OsBuild,
            ScanTime = scanResult.StartedAtUtc,
            ComplianceScore = scanResult.ComplianceScore,
            Grade = scanResult.Grade,
            Summary = new SummaryDto
            {
                TotalChecks = scanResult.Findings.Count,
                PassedCount = scanResult.PassCount,
                FailedCount = scanResult.FailCount,
                WarningCount = scanResult.WarningCount
            },
            Findings = findings
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
        return filePath;
    }

    public async Task<string> GenerateAndSaveCsvReportAsync(ScanResult scanResult, string outputDir, string baseFileName)
    {
        Directory.CreateDirectory(outputDir);
        var filePath = Path.Combine(outputDir, $"{baseFileName}.csv");
        var sb = new StringBuilder();
        sb.AppendLine("CheckId,Name,Category,Severity,Status,CurrentValue,ExpectedValue,CisReference,RiskScore,Recommendation");
        foreach (var f in scanResult.Findings)
        {
            sb.AppendLine(string.Join(",",
                Csv(f.CheckId), Csv(f.Name), Csv(f.Category.ToString()), Csv(f.Severity.ToString()),
                Csv(f.Status.ToString()), Csv(f.CurrentValue), Csv(f.ExpectedValue), Csv(f.CisReference),
                f.RiskScore.ToString(), Csv(f.Recommendation)));
        }
        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
        return filePath;
    }

    // ── NEW Phase 16.1 Methods ──

    public Task<string> GeneratePdfReportAsync(ScanResult scanResult, string outputDir, string baseFileName, ReportGenerationOptions options)
    {
        Directory.CreateDirectory(outputDir);
        var filePath = Path.Combine(outputDir, $"{baseFileName}.pdf");

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeContent);
                page.Footer().AlignCenter().Text(x => { x.Span("Page "); x.CurrentPageNumber(); });
            });

            void ComposeHeader(IContainer container)
            {
                container.Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("DefenDoor Hardening Report").FontSize(20).SemiBold().FontColor(Colors.Blue.Darken2);
                        col.Item().Text($"Target: {scanResult.Hostname}").FontSize(12);
                    });
                    row.ConstantItem(100).AlignRight().Text(scanResult.Grade).FontSize(24).Bold().FontColor(scanResult.ComplianceScore >= 70 ? Colors.Green.Darken2 : Colors.Red.Darken2);
                });
            }

            void ComposeContent(IContainer container)
            {
                container.PaddingVertical(1, Unit.Centimetre).Column(col =>
                {
                    col.Spacing(10);

                    if (options.IncludeExecutiveSummary)
                    {
                        col.Item().Text("Executive Summary").FontSize(16).SemiBold().Underline();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns => { columns.RelativeColumn(); columns.RelativeColumn(); });
                            table.Cell().BorderBottom(1).Padding(5).Text("Compliance Score").Bold();
                            table.Cell().BorderBottom(1).Padding(5).Text($"{scanResult.ComplianceScore}%");
                            table.Cell().BorderBottom(1).Padding(5).Text("Total Checks").Bold();
                            table.Cell().BorderBottom(1).Padding(5).Text(scanResult.Findings.Count.ToString());
                        });
                    }

                    if (options.IncludeTechnicalFindings)
                    {
                        col.Item().PaddingTop(10).Text("Technical Findings").FontSize(16).SemiBold().Underline();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns => { columns.RelativeColumn(2); columns.RelativeColumn(1); columns.RelativeColumn(1); columns.RelativeColumn(1); });
                            table.Header(header =>
                            {
                                header.Cell().Element(CellStyle).Text("Check Name").SemiBold();
                                header.Cell().Element(CellStyle).Text("Severity").SemiBold();
                                header.Cell().Element(CellStyle).Text("Status").SemiBold();
                                header.Cell().Element(CellStyle).Text("Risk").SemiBold();
                                IContainer CellStyle(IContainer c) => c.BorderBottom(1).BorderColor(Colors.Grey.Darken2).Padding(5);
                            });

                            foreach (var f in scanResult.Findings)
                            {
                                table.Cell().Element(CellStyle).Text(f.Name);
                                table.Cell().Element(CellStyle).Text(f.Severity.ToString());
                                table.Cell().Element(CellStyle).Text(f.Status.ToString()).FontColor(f.Status == CheckStatus.Pass ? Colors.Green.Darken2 : Colors.Red.Darken2);
                                table.Cell().Element(CellStyle).Text(f.RiskScore.ToString());
                                IContainer CellStyle(IContainer c) => c.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5);
                            }
                        });
                    }
                });
            }
        }).GeneratePdf(filePath);

        return Task.FromResult(filePath);
    }

    public Task<string> GenerateExcelReportAsync(ScanResult scanResult, string outputDir, string baseFileName, ReportGenerationOptions options)
    {
        Directory.CreateDirectory(outputDir);
        var filePath = Path.Combine(outputDir, $"{baseFileName}.xlsx");

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Findings");

        // Headers
        worksheet.Cell(1, 1).Value = "Check ID";
        worksheet.Cell(1, 2).Value = "Name";
        worksheet.Cell(1, 3).Value = "Severity";
        worksheet.Cell(1, 4).Value = "Status";
        worksheet.Cell(1, 5).Value = "Current Value";
        worksheet.Cell(1, 6).Value = "Expected Value";
        worksheet.Cell(1, 7).Value = "Risk Score";

        var headerRange = worksheet.Range("A1:G1");
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f172a");
        headerRange.Style.Font.FontColor = XLColor.FromHtml("#06b6d4");

        int row = 2;
        foreach (var f in scanResult.Findings)
        {
            worksheet.Cell(row, 1).Value = f.CheckId;
            worksheet.Cell(row, 2).Value = f.Name;
            worksheet.Cell(row, 3).Value = f.Severity.ToString();
            worksheet.Cell(row, 4).Value = f.Status.ToString();
            worksheet.Cell(row, 5).Value = f.CurrentValue;
            worksheet.Cell(row, 6).Value = f.ExpectedValue;
            worksheet.Cell(row, 7).Value = f.RiskScore;

            // Conditional Formatting for Status
            if (f.Status == CheckStatus.Pass)
                worksheet.Cell(row, 4).Style.Fill.BackgroundColor = XLColor.FromHtml("#10b981");
            else if (f.Status == CheckStatus.Fail)
                worksheet.Cell(row, 4).Style.Fill.BackgroundColor = XLColor.FromHtml("#ef4444");

            row++;
        }

        worksheet.Columns().AdjustToContents();
        workbook.SaveAs(filePath);
        return Task.FromResult(filePath);
    }

    public async Task<string> GenerateZipBundleAsync(ScanResult scanResult, string outputDir, string baseFileName, ReportGenerationOptions options)
    {
        Directory.CreateDirectory(outputDir);
        var zipPath = Path.Combine(outputDir, $"{baseFileName}.zip");
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var htmlPath = await GenerateAndSaveReportAsync(scanResult, tempDir, "Report");
            var jsonPath = await GenerateAndSaveJsonReportAsync(scanResult, tempDir, "Report");

            if (options.IncludeRawEvidence)
            {
                var evidencePath = Path.Combine(tempDir, "RawEvidence.json");
                var evidenceData = JsonSerializer.Serialize(scanResult.Findings.Select(f => new { f.CheckId, f.CurrentValue, f.SourceCommand }));
                await File.WriteAllTextAsync(evidencePath, evidenceData, Encoding.UTF8);
            }

            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(tempDir, zipPath, CompressionLevel.Optimal, false);

            return zipPath;
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { /* Ignore cleanup errors */ }
            }
        }
    }

    // ── Helpers ──
    private static string SummaryItem(string label, string value) =>
        $"<div class=\"summary-item\"><div class=\"label\">{Escape(label)}</div><div class=\"value\">{Escape(value)}</div></div>";

    private static string Escape(string? s) =>
        System.Net.WebUtility.HtmlEncode(string.IsNullOrEmpty(s) ? string.Empty : s);

    private static string Csv(string? s)
    {
        var v = string.IsNullOrEmpty(s) ? string.Empty : s;
        return $"\"{v.Replace("\"", "\"\"")}\"";
    }

    // ── DTOs for JSON Serialization ──
    private sealed class ReportDto
    {
        public string ReportType { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; }
        public string Hostname { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string OsVersion { get; set; } = string.Empty;
        public int OsBuild { get; set; }
        public DateTime ScanTime { get; set; }
        public int ComplianceScore { get; set; }
        public string Grade { get; set; } = string.Empty;
        public SummaryDto Summary { get; set; } = new();
        public List<FindingDto> Findings { get; set; } = new();
    }
    private sealed class SummaryDto
    {
        public int TotalChecks { get; set; }
        public int PassedCount { get; set; }
        public int FailedCount { get; set; }
        public int WarningCount { get; set; }
    }
    private sealed class FindingDto
    {
        public string CheckId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string CurrentValue { get; set; } = string.Empty;
        public string ExpectedValue { get; set; } = string.Empty;
        public string CisReference { get; set; } = string.Empty;
        public int RiskScore { get; set; }
        public string Recommendation { get; set; } = string.Empty;
    }
}