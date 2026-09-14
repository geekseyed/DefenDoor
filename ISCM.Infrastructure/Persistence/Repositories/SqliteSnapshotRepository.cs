using System.Text.Json;
using ISCM.Application.Interfaces;
using ISCM.Application.Snapshots;
using ISCM.Domain.Entities;
using ISCM.Domain.Enums;
using ISCM.Infrastructure.Persistence.Records;
using ISCM.Infrastructure.Persistence.Serialization;
using Microsoft.EntityFrameworkCore;

namespace ISCM.Infrastructure.Persistence.Repositories;

public class SqliteSnapshotRepository : ISnapshotRepository
{
    private readonly DefenDoorDbContext _dbContext;
    private readonly ISnapshotMapper _mapper;

    public SqliteSnapshotRepository(DefenDoorDbContext dbContext, ISnapshotMapper mapper)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    public async Task SaveAsync(ScanSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

        // FIX: CompletedAtUtc is DateTime (not nullable), check for default value
        if (snapshot.CompletedAtUtc == default)
            throw new InvalidOperationException("Cannot save an incomplete snapshot.");

        if (await _dbContext.Snapshots.AnyAsync(s => s.Id == snapshot.SnapshotId, cancellationToken))
            throw new InvalidOperationException($"Snapshot with ID {snapshot.SnapshotId} already exists.");

        if (await _dbContext.Snapshots.AnyAsync(s => s.ScanId == snapshot.ScanId, cancellationToken))
            throw new InvalidOperationException($"Snapshot with ScanId {snapshot.ScanId} already exists.");

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var record = new SnapshotRecord
            {
                Id = snapshot.SnapshotId,
                ScanId = snapshot.ScanId,
                AssetId = snapshot.AssetId,
                Hostname = snapshot.Hostname,
                IpAddress = snapshot.IpAddress,
                MacAddress = snapshot.MacAddress,
                OsVersion = snapshot.OsVersion,
                OsBuild = snapshot.OsBuild,
                ScanMode = snapshot.ScanMode.ToString(),
                BaselineId = snapshot.BaselineId,
                BaselineName = snapshot.BaselineName,
                ScannerVersion = snapshot.ScannerVersion,
                StartedAtUtc = snapshot.StartedAtUtc,
                CompletedAtUtc = snapshot.CompletedAtUtc,
                OverallStatus = snapshot.OverallStatus.ToString(),
                ComplianceScore = snapshot.ComplianceScore,
                Grade = snapshot.Grade,
                PassCount = snapshot.PassCount,
                FailCount = snapshot.FailCount,
                WarningCount = snapshot.WarningCount,
                ErrorCount = snapshot.ErrorCount,
                TotalControlCount = snapshot.TotalControlCount,
                TotalSubControlCount = snapshot.TotalSubControlCount,
                IntegrityHash = snapshot.IntegrityHash,
                SchemaVersion = snapshot.SchemaVersion,
                PersistedAtUtc = DateTime.UtcNow
            };

            _dbContext.Snapshots.Add(record);

            foreach (var finding in snapshot.Findings)
            {
                _dbContext.Findings.Add(new FindingRecord
                {
                    Id = Guid.NewGuid(),
                    SnapshotId = snapshot.SnapshotId,
                    CheckId = finding.CheckId,
                    SubControlId = finding.SubControlId,
                    Name = finding.Name,
                    Category = finding.Category.ToString(),
                    Severity = finding.Severity.ToString(),
                    Status = finding.Status.ToString(),
                    CurrentValue = finding.CurrentValue,
                    ExpectedValue = finding.ExpectedValue,
                    CisReference = finding.CisReference,
                    RiskScore = finding.RiskScore,
                    IsSuppressed = finding.IsSuppressed,
                    IsFalsePositive = finding.IsFalsePositive
                });
            }

            foreach (var control in snapshot.Controls)
            {
                foreach (var subControl in control.SubControls)
                {
                    var payload = new
                    {
                        ControlId = control.ControlId,
                        ControlStatus = control.Status.ToString(),
                        SubControlId = subControl.SubControlId,
                        SubControlStatus = subControl.Status.ToString(),
                        EvaluatedAt = subControl.EvaluatedAt,
                        ExpectedValue = subControl.ExpectedValue,
                        ActualValue = subControl.ActualValue,
                        EvaluationReason = subControl.EvaluationReason,
                        EvidenceItems = subControl.EvidenceItems.Select(e => new
                        {
                            e.EvidenceId,
                            e.SourceType,
                            e.SourceName,
                            e.AcquisitionCommand,
                            e.AcquisitionArguments,
                            e.MachineIdentity,
                            e.CollectedAtUtc,
                            e.CollectionDurationMs,
                            e.RawOutput,
                            e.ParsedValue,
                            e.NormalizedValue,
                            e.ExpectedValue,
                            e.ValueType,
                            e.Evaluation,
                            e.EvaluationReason,
                            e.Error,
                            e.Fingerprint,
                            e.LifecycleState
                        })
                    };

                    var payloadJson = JsonSerializer.Serialize(payload, DefenDoorJsonOptions.Default);

                    _dbContext.EvidencePayloads.Add(new EvidencePayloadRecord
                    {
                        Id = Guid.NewGuid(),
                        SnapshotId = snapshot.SnapshotId,
                        ControlId = control.ControlId,
                        SubControlId = subControl.SubControlId,
                        PayloadJson = payloadJson
                    });
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ScanSnapshot?> GetAsync(Guid snapshotId, CancellationToken cancellationToken = default)
    {
        var record = await _dbContext.Snapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == snapshotId, cancellationToken);

        if (record == null) return null;

        return await ReconstructSnapshotAsync(record, cancellationToken);
    }

    public async Task<ScanSnapshot?> GetByScanIdAsync(string scanId, CancellationToken cancellationToken = default)
    {
        var record = await _dbContext.Snapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ScanId == scanId, cancellationToken);

        if (record == null) return null;

        return await ReconstructSnapshotAsync(record, cancellationToken);
    }

    public async Task<IReadOnlyList<ScanSnapshot>> GetByAssetAsync(string hostname, CancellationToken cancellationToken = default)
    {
        var records = await _dbContext.Snapshots
            .AsNoTracking()
            .Where(s => s.Hostname == hostname)
            .OrderByDescending(s => s.CompletedAtUtc)
            .ToListAsync(cancellationToken);

        var snapshots = new List<ScanSnapshot>();
        foreach (var record in records)
        {
            snapshots.Add(await ReconstructSnapshotAsync(record, cancellationToken));
        }
        return snapshots;
    }

    public async Task<ScanSnapshot?> GetLatestByAssetAsync(string hostname, CancellationToken cancellationToken = default)
    {
        var record = await _dbContext.Snapshots
            .AsNoTracking()
            .Where(s => s.Hostname == hostname)
            .OrderByDescending(s => s.CompletedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (record == null) return null;

        return await ReconstructSnapshotAsync(record, cancellationToken);
    }

    public async Task<IReadOnlyList<ScanSnapshot>> GetRecentAsync(int limit = 10, CancellationToken cancellationToken = default)
    {
        var records = await _dbContext.Snapshots
            .AsNoTracking()
            .OrderByDescending(s => s.CompletedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var snapshots = new List<ScanSnapshot>();
        foreach (var record in records)
        {
            snapshots.Add(await ReconstructSnapshotAsync(record, cancellationToken));
        }
        return snapshots;
    }

    public async Task<IReadOnlyList<SnapshotSummary>> ListSummariesAsync(string? hostname = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Snapshots.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(hostname))
            query = query.Where(s => s.Hostname == hostname);

        return await query
            .OrderByDescending(s => s.CompletedAtUtc)
            .Select(s => new SnapshotSummary
            {
                SnapshotId = s.Id,
                ScanId = s.ScanId,
                AssetId = s.AssetId,
                Hostname = s.Hostname,
                OsVersion = s.OsVersion,
                CompletedAtUtc = s.CompletedAtUtc,
                Grade = s.Grade,
                ComplianceScore = s.ComplianceScore,
                OverallStatus = Enum.Parse<CheckStatus>(s.OverallStatus),
                PassCount = s.PassCount,
                FailCount = s.FailCount,
                TotalControlCount = s.TotalControlCount
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid snapshotId, CancellationToken cancellationToken = default)
    {
        var record = await _dbContext.Snapshots.FindAsync(new object[] { snapshotId }, cancellationToken);
        if (record == null) return false;

        var findings = await _dbContext.Findings.Where(f => f.SnapshotId == snapshotId).ToListAsync(cancellationToken);
        var payloads = await _dbContext.EvidencePayloads.Where(e => e.SnapshotId == snapshotId).ToListAsync(cancellationToken);

        _dbContext.Findings.RemoveRange(findings);
        _dbContext.EvidencePayloads.RemoveRange(payloads);
        _dbContext.Snapshots.Remove(record);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Snapshots.CountAsync(cancellationToken);
    }

    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _dbContext.Snapshots.AnyAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }
    // ═══════════════════════════════════════════════════════════
    // Phase 15.5: ExistsAsync - Duplicate detection for import
    // ═══════════════════════════════════════════════════════════

    public async Task<bool> ExistsAsync(Guid snapshotId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Snapshots
            .AsNoTracking()
            .AnyAsync(s => s.Id == snapshotId, cancellationToken);
    }

    private async Task<ScanSnapshot> ReconstructSnapshotAsync(SnapshotRecord record, CancellationToken cancellationToken)
    {
        var findings = await _dbContext.Findings
            .AsNoTracking()
            .Where(f => f.SnapshotId == record.Id)
            .ToListAsync(cancellationToken);

        var payloads = await _dbContext.EvidencePayloads
            .AsNoTracking()
            .Where(e => e.SnapshotId == record.Id)
            .ToListAsync(cancellationToken);

        var controlGroups = payloads
            .GroupBy(p => p.ControlId)
            .Select(g =>
            {
                var subControls = g.Select(p =>
                {
                    var payloadData = JsonSerializer.Deserialize<JsonElement>(p.PayloadJson, DefenDoorJsonOptions.Default);

                    var evidenceItems = new List<EvidenceSnapshot>();
                    if (payloadData.TryGetProperty("evidenceItems", out var evidenceArray) ||
                        payloadData.TryGetProperty("EvidenceItems", out evidenceArray))
                    {
                        foreach (var e in evidenceArray.EnumerateArray())
                        {
                            evidenceItems.Add(new EvidenceSnapshot
                            {
                                EvidenceId = GetProp(e, "EvidenceId", "evidenceId")?.GetString() ?? string.Empty,
                                SubControlId = p.SubControlId,
                                TechnicalCheckId = string.Empty,
                                SourceType = Enum.TryParse<EvidenceSourceType>(GetProp(e, "SourceType", "sourceType")?.GetString(), out var st) ? st : EvidenceSourceType.Unknown,
                                SourceName = GetProp(e, "SourceName", "sourceName")?.GetString() ?? string.Empty,
                                AcquisitionCommand = GetProp(e, "AcquisitionCommand", "acquisitionCommand")?.GetString() ?? string.Empty,
                                AcquisitionArguments = GetProp(e, "AcquisitionArguments", "acquisitionArguments")?.GetString(),
                                MachineIdentity = GetProp(e, "MachineIdentity", "machineIdentity")?.GetString() ?? string.Empty,
                                CollectedAtUtc = GetProp(e, "CollectedAtUtc", "collectedAtUtc")?.GetDateTime() ?? DateTime.MinValue,
                                CollectionDurationMs = GetProp(e, "CollectionDurationMs", "collectionDurationMs")?.GetInt32() ?? 0,
                                RawOutput = GetProp(e, "RawOutput", "rawOutput")?.GetString(),
                                ParsedValue = GetProp(e, "ParsedValue", "parsedValue")?.GetString(),
                                NormalizedValue = GetProp(e, "NormalizedValue", "normalizedValue")?.GetString(),
                                ExpectedValue = GetProp(e, "ExpectedValue", "expectedValue")?.GetString(),
                                ValueType = Enum.TryParse<EvidenceValueType>(GetProp(e, "ValueType", "valueType")?.GetString(), out var vt) ? vt : EvidenceValueType.Unknown,
                                Evaluation = Enum.TryParse<CheckStatus>(GetProp(e, "Evaluation", "evaluation")?.GetString(), out var ev) ? ev : CheckStatus.NotScanned,
                                EvaluationReason = GetProp(e, "EvaluationReason", "evaluationReason")?.GetString(),
                                Error = GetProp(e, "Error", "error")?.GetString(),
                                Fingerprint = GetProp(e, "Fingerprint", "fingerprint")?.GetString() ?? string.Empty,
                                LifecycleState = Enum.TryParse<EvidenceLifecycleState>(GetProp(e, "LifecycleState", "lifecycleState")?.GetString(), out var ls) ? ls : EvidenceLifecycleState.Live
                            });
                        }
                    }

                    return new SubControlSnapshot
                    {
                        SubControlId = p.SubControlId,
                        ParentControlId = p.ControlId,
                        Status = Enum.TryParse<CheckStatus>(payloadData.GetProperty("SubControlStatus").GetString(), out var ss) ? ss : CheckStatus.Unknown,
                        EvaluatedAt = payloadData.GetProperty("EvaluatedAt").GetDateTime(),
                        ExpectedValue = payloadData.TryGetProperty("ExpectedValue", out var evProp) ? evProp.GetString() : null,
                        ActualValue = payloadData.TryGetProperty("ActualValue", out var avProp) ? avProp.GetString() : null,
                        EvaluationReason = payloadData.TryGetProperty("EvaluationReason", out var erProp) ? erProp.GetString() : null,
                        EvidenceItems = evidenceItems
                    };
                }).ToList();

                return new ControlSnapshot
                {
                    ControlId = g.Key,
                    ParentControlId = string.Empty,
                    Status = subControls.Any() ? subControls.First().Status : CheckStatus.Unknown,
                    EvaluatedAt = subControls.Any() ? subControls.First().EvaluatedAt : DateTime.MinValue,
                    SubControls = subControls
                };
            })
            .ToList();

        return new ScanSnapshot
        {
            SnapshotId = record.Id,
            ScanId = record.ScanId,
            AssetId = record.AssetId,
            Hostname = record.Hostname,
            IpAddress = record.IpAddress,
            MacAddress = record.MacAddress,
            OsVersion = record.OsVersion,
            OsBuild = record.OsBuild,
            ScanMode = Enum.TryParse<ScanMode>(record.ScanMode, out var sm) ? sm : ScanMode.Full,
            BaselineId = record.BaselineId,
            BaselineName = record.BaselineName,
            ScannerVersion = record.ScannerVersion,
            StartedAtUtc = record.StartedAtUtc,
            CompletedAtUtc = record.CompletedAtUtc,
            OverallStatus = Enum.TryParse<CheckStatus>(record.OverallStatus, out var os) ? os : CheckStatus.Unknown,
            ComplianceScore = record.ComplianceScore,
            Grade = record.Grade,
            PassCount = record.PassCount,
            FailCount = record.FailCount,
            WarningCount = record.WarningCount,
            ErrorCount = record.ErrorCount,
            TotalControlCount = record.TotalControlCount,
            TotalSubControlCount = record.TotalSubControlCount,
            Controls = controlGroups,
            Findings = findings.Select(f => new FindingSnapshot
            {
                CheckId = f.CheckId,
                SubControlId = f.SubControlId,
                Name = f.Name,
                // FIX: CheckCategory.Other doesn't exist, use default
                Category = Enum.TryParse<CheckCategory>(f.Category, out var cat) ? cat : default,
                Severity = Enum.TryParse<CheckSeverity>(f.Severity, out var sev) ? sev : CheckSeverity.Low,
                Status = Enum.TryParse<CheckStatus>(f.Status, out var st) ? st : CheckStatus.Unknown,
                CurrentValue = f.CurrentValue,
                ExpectedValue = f.ExpectedValue,
                CisReference = f.CisReference,
                RiskScore = f.RiskScore,
                SourceType = string.Empty,
                SourceCommand = string.Empty,
                IsSuppressed = f.IsSuppressed,
                IsFalsePositive = f.IsFalsePositive
            }).ToList(),
            IntegrityHash = record.IntegrityHash,
            SchemaVersion = record.SchemaVersion
        };
    }

    private static JsonElement? GetProp(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop))
                return prop;
        }
        return null;
    }
}