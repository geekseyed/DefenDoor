using ISCM.Application.Snapshots;

namespace ISCM.Application.Interfaces;

/// <summary>
/// Persistence abstraction for scan snapshots.
/// 
/// Phase 13.2: Defines the contract for storing and retrieving immutable
/// historical scan snapshots. This interface MUST NOT depend on any specific
/// persistence technology (SQLite, PostgreSQL, SQL Server, file system).
/// 
/// Design principles:
/// - Snapshots are immutable historical records
/// - Repository handles persistence concerns (transactions, migrations)
/// - Application layer remains database-agnostic
/// - Future providers (PostgreSQL, SQL Server) implement this same interface
/// 
/// Thread-safety: Implementations must be thread-safe for concurrent reads.
/// Write operations should be serialized at the infrastructure level.
/// </summary>
public interface ISnapshotRepository
{
    /// <summary>
    /// Persists a new immutable snapshot.
    /// 
    /// Contract:
    /// - Snapshot must be complete (CompletedAtUtc has value)
    /// - SnapshotId must be unique; duplicate IDs throw InvalidOperationException
    /// - Operation is atomic: either all data persists or nothing persists
    /// - After save, snapshot is immutable and cannot be modified through this repository
    /// </summary>
    /// <param name="snapshot">The completed scan snapshot to persist</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <exception cref="ArgumentNullException">If snapshot is null</exception>
    /// <exception cref="InvalidOperationException">If snapshot already exists or is incomplete</exception>
    Task SaveAsync(ScanSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a snapshot by its unique identifier.
    /// </summary>
    /// <param name="snapshotId">The snapshot's unique identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The snapshot if found; null otherwise</returns>
    Task<ScanSnapshot?> GetAsync(Guid snapshotId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a snapshot by its original ScanId (correlation identifier).
    /// </summary>
    /// <param name="scanId">The original scan correlation ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The snapshot if found; null otherwise</returns>
    Task<ScanSnapshot?> GetByScanIdAsync(string scanId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all snapshots for a specific asset (identified by hostname).
    /// Results are ordered by CompletedAtUtc descending (most recent first).
    /// </summary>
    /// <param name="hostname">The asset hostname</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of snapshots, possibly empty</returns>
    Task<IReadOnlyList<ScanSnapshot>> GetByAssetAsync(string hostname, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the most recent snapshot for a specific asset.
    /// </summary>
    /// <param name="hostname">The asset hostname</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The latest snapshot if any exist; null otherwise</returns>
    Task<ScanSnapshot?> GetLatestByAssetAsync(string hostname, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the most recent snapshots across all assets.
    /// </summary>
    /// <param name="limit">Maximum number of snapshots to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of recent snapshots, ordered by CompletedAtUtc descending</returns>
    Task<IReadOnlyList<ScanSnapshot>> GetRecentAsync(int limit = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists snapshot metadata (without full evidence payloads) for efficient UI rendering.
    /// </summary>
    /// <param name="hostname">Optional filter by hostname</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of snapshot summaries</returns>
    Task<IReadOnlyList<SnapshotSummary>> ListSummariesAsync(string? hostname = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a snapshot and all associated data (findings, evidence payloads).
    /// This operation is irreversible and should be used with caution.
    /// </summary>
    /// <param name="snapshotId">The snapshot ID to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if deleted; false if not found</returns>
    Task<bool> DeleteAsync(Guid snapshotId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the total count of persisted snapshots.
    /// </summary>
    Task<int> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies repository health and accessibility.
    /// </summary>
    /// <returns>True if repository is operational</returns>
    Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Phase 15.5: Checks if a snapshot with the given ID already exists.
    /// Used for duplicate detection during import operations.
    /// </summary>
    /// <param name="snapshotId">The snapshot ID to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if a snapshot with this ID exists; false otherwise</returns>
    Task<bool> ExistsAsync(Guid snapshotId, CancellationToken cancellationToken = default);
}