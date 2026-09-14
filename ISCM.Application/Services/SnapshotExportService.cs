using ISCM.Application.Interfaces;
using ISCM.Application.Snapshots;
using ISCM.Domain.ValueObjects;
using System.Text.Json;

namespace ISCM.Application.Services;

/// <summary>
/// Phase 15.5: Implementation of snapshot export/import functionality.
/// 
/// Handles file-based transfer of immutable scan snapshots between
/// DefenDoor instances for AirGapped environments and cross-machine backup.
/// 
/// Features:
/// - JSON-based export with metadata envelope
/// - Integrity validation on import
/// - Duplicate detection (by SnapshotId and ScanId)
/// - Graceful error handling - one file failure doesn't stop batch import
/// </summary>
public class SnapshotExportService : ISnapshotExportService
{
    private readonly ISnapshotRepository _snapshotRepository;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public SnapshotExportService(ISnapshotRepository snapshotRepository)
    {
        _snapshotRepository = snapshotRepository ?? throw new ArgumentNullException(nameof(snapshotRepository));
    }

    public async Task<string> ExportSnapshotAsync(
        Guid snapshotId,
        string outputDir,
        string? exportedBy = null,
        CancellationToken cancellationToken = default)
    {
        if (snapshotId == Guid.Empty)
            throw new ArgumentException("SnapshotId cannot be empty", nameof(snapshotId));

        if (string.IsNullOrWhiteSpace(outputDir))
            throw new ArgumentException("Output directory cannot be empty", nameof(outputDir));

        Console.WriteLine($"[INFO] Exporting snapshot {snapshotId} to {outputDir}");

        var snapshot = await _snapshotRepository.GetAsync(snapshotId, cancellationToken);
        if (snapshot == null)
        {
            throw new InvalidOperationException($"Snapshot {snapshotId} not found in repository.");
        }

        var package = new SnapshotExportPackage(snapshot, exportedBy ?? "System");

        Directory.CreateDirectory(outputDir);

        var safeHostname = string.IsNullOrEmpty(snapshot.Hostname) ? "unknown" :
            snapshot.Hostname.Replace(" ", "_").Replace("/", "_").Replace("\\", "_");
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var fileName = $"DefenDoor_Snapshot_{safeHostname}_{timestamp}_{snapshotId:N}.defendoor.json";
        var filePath = Path.Combine(outputDir, fileName);

        try
        {
            var json = JsonSerializer.Serialize(package, JsonOptions);
            await File.WriteAllTextAsync(filePath, json, System.Text.Encoding.UTF8, cancellationToken);

            Console.WriteLine($"[INFO] Snapshot {snapshotId} exported to {filePath}");
            return filePath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to export snapshot {snapshotId}: {ex.Message}");
            throw new IOException($"Failed to write export file: {ex.Message}", ex);
        }
    }

    public async Task<SnapshotImportResult> ImportSnapshotAsync(
        string filePath,
        string? importedBy = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return SnapshotImportResult.InvalidFormat(filePath ?? "", "File path cannot be empty");

        if (!File.Exists(filePath))
            return SnapshotImportResult.Failed(filePath, "File not found");

        Console.WriteLine($"[INFO] Importing snapshot from {filePath}");

        try
        {
            var json = await File.ReadAllTextAsync(filePath, System.Text.Encoding.UTF8, cancellationToken);

            SnapshotExportPackage? package;
            try
            {
                package = JsonSerializer.Deserialize<SnapshotExportPackage>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"[WARNING] Failed to deserialize export package: {ex.Message}");
                return SnapshotImportResult.InvalidFormat(filePath, $"Invalid JSON format: {ex.Message}");
            }

            if (package == null || !package.IsValid())
            {
                return SnapshotImportResult.InvalidFormat(filePath, "Invalid or incomplete export package");
            }

            var snapshot = package.Snapshot;

            if (snapshot.SnapshotId == Guid.Empty)
                return SnapshotImportResult.IntegrityFailed(filePath, "Snapshot missing SnapshotId");

            if (string.IsNullOrEmpty(snapshot.ScanId))
                return SnapshotImportResult.IntegrityFailed(filePath, "Snapshot missing ScanId");

            if (snapshot.CompletedAtUtc == default)
                return SnapshotImportResult.IntegrityFailed(filePath, "Snapshot missing CompletedAtUtc");

            var existsBySnapshotId = await _snapshotRepository.ExistsAsync(snapshot.SnapshotId, cancellationToken);
            if (existsBySnapshotId)
            {
                return SnapshotImportResult.Duplicate(filePath, snapshot.SnapshotId);
            }

            if (!string.IsNullOrEmpty(snapshot.ScanId))
            {
                var existingByScanId = await _snapshotRepository.GetByScanIdAsync(snapshot.ScanId, cancellationToken);
                if (existingByScanId != null)
                {
                    return SnapshotImportResult.Duplicate(filePath, existingByScanId.SnapshotId);
                }
            }

            await _snapshotRepository.SaveAsync(snapshot, cancellationToken);

            Console.WriteLine($"[INFO] Snapshot {snapshot.SnapshotId} imported successfully (Host: {snapshot.Hostname}, Score: {snapshot.ComplianceScore})");

            return SnapshotImportResult.Success(filePath, snapshot);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to import snapshot from {filePath}: {ex.Message}");
            return SnapshotImportResult.Failed(filePath, $"Import failed: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<SnapshotImportResult>> ImportMultipleAsync(
        IEnumerable<string> filePaths,
        string? importedBy = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<SnapshotImportResult>();

        foreach (var filePath in filePaths)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var result = await ImportSnapshotAsync(filePath, importedBy, cancellationToken);
            results.Add(result);
        }

        return results;
    }
}