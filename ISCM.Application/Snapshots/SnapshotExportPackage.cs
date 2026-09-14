using ISCM.Application.Snapshots;

namespace ISCM.Domain.ValueObjects;

/// <summary>
/// Phase 15.5: Wrapper for snapshot export/import operations.
/// 
/// Encapsulates a ScanSnapshot with export metadata for file-based transfer
/// between DefenDoor instances (AirGapped environments, cross-machine backup).
/// 
/// Design principles:
/// - Format versioning for future compatibility
/// - Export metadata for audit trail
/// - Integrity preservation via ScanSnapshot.IntegrityHash
/// </summary>
public sealed class SnapshotExportPackage
{
    /// <summary>
    /// Export format identifier for versioning.
    /// Format: "DefenDoor.Snapshot.v{version}"
    /// </summary>
    public string ExportFormat { get; init; } = "DefenDoor.Snapshot.v1";

    /// <summary>
    /// When this snapshot was exported to file.
    /// </summary>
    public DateTime ExportedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Who performed the export (for audit trail).
    /// </summary>
    public string ExportedBy { get; init; } = string.Empty;

    /// <summary>
    /// The immutable scan snapshot being exported.
    /// </summary>
    public ScanSnapshot Snapshot { get; init; } = null!;

    /// <summary>
    /// Optional export notes or description.
    /// </summary>
    public string? ExportNotes { get; init; }

    public SnapshotExportPackage()
    {
    }

    public SnapshotExportPackage(ScanSnapshot snapshot, string exportedBy, string? notes = null)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        ExportedBy = exportedBy ?? "System";
        ExportNotes = notes;
        ExportedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Validates the export package structure.
    /// </summary>
    public bool IsValid()
    {
        return !string.IsNullOrEmpty(ExportFormat) &&
               ExportFormat.StartsWith("DefenDoor.Snapshot.v") &&
               Snapshot != null &&
               Snapshot.SnapshotId != Guid.Empty &&
               Snapshot.CompletedAtUtc != default;
    }

    public override string ToString()
        => $"ExportPackage {ExportFormat} | {Snapshot?.SnapshotId:N} | Exported: {ExportedAtUtc:yyyy-MM-dd HH:mm}";
}