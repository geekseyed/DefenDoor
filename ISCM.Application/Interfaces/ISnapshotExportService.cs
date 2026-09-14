using ISCM.Application.Snapshots;

namespace ISCM.Application.Interfaces;

/// <summary>
/// Phase 15.5: Service for exporting and importing ScanSnapshots.
/// 
/// Supports file-based transfer of immutable scan snapshots between
/// DefenDoor instances for AirGapped environments, cross-machine backup,
/// and historical scan archival.
/// 
/// Design principles:
/// - Export format versioned for future compatibility
/// - Import validates integrity hash before persistence
/// - Duplicate detection prevents snapshot proliferation
/// - Atomic operations - import either succeeds fully or fails cleanly
/// </summary>
public interface ISnapshotExportService
{
    /// <summary>
    /// Exports a snapshot to a JSON file.
    /// </summary>
    /// <param name="snapshotId">The snapshot to export</param>
    /// <param name="outputDir">Target directory for the export file</param>
    /// <param name="exportedBy">Operator who initiated the export (for audit)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Full path of the created export file</returns>
    /// <exception cref="InvalidOperationException">If snapshot not found</exception>
    /// <exception cref="IOException">If file cannot be written</exception>
    Task<string> ExportSnapshotAsync(
        Guid snapshotId,
        string outputDir,
        string? exportedBy = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports a snapshot from a JSON file into the local repository.
    /// </summary>
    /// <param name="filePath">Path to the export file</param>
    /// <param name="importedBy">Operator who initiated the import (for audit)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Import result with status and details</returns>
    Task<SnapshotImportResult> ImportSnapshotAsync(
        string filePath,
        string? importedBy = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch import multiple snapshot files.
    /// Each file is processed independently - one failure does not stop others.
    /// </summary>
    /// <param name="filePaths">Paths to export files</param>
    /// <param name="importedBy">Operator who initiated the import (for audit)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of import results, one per file</returns>
    Task<IReadOnlyList<SnapshotImportResult>> ImportMultipleAsync(
        IEnumerable<string> filePaths,
        string? importedBy = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a single snapshot import operation.
/// </summary>
public sealed class SnapshotImportResult
{
    public string FilePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public SnapshotImportStatus Status { get; init; }
    public string Message { get; init; } = string.Empty;
    public Guid? SnapshotId { get; init; }
    public string? Hostname { get; init; }
    public int? ComplianceScore { get; init; }

    public static SnapshotImportResult Success(
        string filePath,
        ScanSnapshot snapshot) => new()
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            Status = SnapshotImportStatus.Success,
            Message = $"Snapshot imported successfully",
            SnapshotId = snapshot.SnapshotId,
            Hostname = snapshot.Hostname,
            ComplianceScore = snapshot.ComplianceScore
        };

    public static SnapshotImportResult Duplicate(
        string filePath,
        Guid existingId) => new()
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            Status = SnapshotImportStatus.Duplicate,
            Message = $"Snapshot already exists (ID: {existingId:N})",
            SnapshotId = existingId
        };

    public static SnapshotImportResult InvalidFormat(
        string filePath,
        string reason) => new()
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            Status = SnapshotImportStatus.InvalidFormat,
            Message = reason
        };

    public static SnapshotImportResult IntegrityFailed(
        string filePath,
        string reason) => new()
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            Status = SnapshotImportStatus.IntegrityFailed,
            Message = reason
        };

    public static SnapshotImportResult Failed(
        string filePath,
        string reason) => new()
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            Status = SnapshotImportStatus.Failed,
            Message = reason
        };
}

/// <summary>
/// Status codes for snapshot import operations.
/// </summary>
public enum SnapshotImportStatus
{
    /// <summary>Snapshot imported and persisted successfully.</summary>
    Success,

    /// <summary>Snapshot already exists in local repository (by SnapshotId or ScanId).</summary>
    Duplicate,

    /// <summary>File is not a valid DefenDoor export package.</summary>
    InvalidFormat,

    /// <summary>Snapshot integrity hash does not match - file may be corrupted or tampered.</summary>
    IntegrityFailed,

    /// <summary>Import failed due to I/O or database error.</summary>
    Failed
}