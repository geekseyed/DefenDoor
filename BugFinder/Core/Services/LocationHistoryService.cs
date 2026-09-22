using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-11.2: Location History Service
/// Manages persistence and retrieval of failure records by code location.
/// Storage: JSON file in .bugfinder/history/location_history.json.
/// </summary>
public class LocationHistoryService
{
    private readonly string _locationHistoryFilePath;
    private readonly object _lockObj = new();

    public LocationHistoryService(string? workingDirectory = null)
    {
        var baseDir = workingDirectory ?? Directory.GetCurrentDirectory();
        var historyDir = Path.Combine(baseDir, ".bugfinder", "history");

        if (!Directory.Exists(historyDir))
        {
            Directory.CreateDirectory(historyDir);
        }

        _locationHistoryFilePath = Path.Combine(historyDir, "location_history.json");

        if (!File.Exists(_locationHistoryFilePath))
        {
            SaveStore(new List<CodeLocationRecord>());
        }
    }

    /// <summary>
    /// BF-11.2 - Stage 1: Record a failure at a specific code location.
    /// </summary>
    public async Task RecordLocationFailureAsync(CodeLocationRecord record)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        if (string.IsNullOrEmpty(record.FilePath))
            throw new ArgumentException("FilePath is required.", nameof(record));

        await Task.Run(() =>
        {
            lock (_lockObj)
            {
                var records = LoadStore();
                records.Add(record);

                // Prune old records if needed (keep last 5000 location events)
                if (records.Count > 5000)
                {
                    records = records.OrderByDescending(r => r.RecordedAt).Take(5000).ToList();
                }

                SaveStore(records);
            }
        });
    }

    /// <summary>
    /// BF-11.2 - Stage 2: Retrieve history for a specific code location (File + Method + Line).
    /// </summary>
    public async Task<LocationHistoryResult> GetLocationHistoryAsync(
        string filePath,
        string? methodName = null,
        int? lineNumber = null)
    {
        if (string.IsNullOrEmpty(filePath))
            return new LocationHistoryResult();

        return await Task.Run(() =>
        {
            lock (_lockObj)
            {
                var records = LoadStore();

                var matches = records.Where(r =>
                    r.FilePath == filePath &&
                    (methodName == null || r.MethodName == methodName) &&
                    (lineNumber == null || r.LineNumber == lineNumber)
                ).OrderByDescending(r => r.RecordedAt).ToList();

                if (matches.Count == 0)
                {
                    return new LocationHistoryResult
                    {
                        FilePath = filePath,
                        MethodName = methodName,
                        TotalFailuresAtLocation = 0
                    };
                }

                return new LocationHistoryResult
                {
                    FilePath = filePath,
                    MethodName = methodName,
                    TotalFailuresAtLocation = matches.Count,
                    FirstFailure = matches.Min(r => r.RecordedAt),
                    LastFailure = matches.Max(r => r.RecordedAt),
                    RecentRecords = matches.Take(10).ToList(),
                    AssociatedFailureSignatures = matches.Select(r => r.FailureSignature).Distinct().ToList()
                };
            }
        });
    }

    /// <summary>
    /// BF-11.2 - Stage 3: Identify hotspots (locations with frequent failures).
    /// </summary>
    public async Task<List<LocationHistoryResult>> GetHotspotsAsync(int threshold = 3)
    {
        return await Task.Run(() =>
        {
            lock (_lockObj)
            {
                var records = LoadStore();

                var grouped = records.GroupBy(r => new { r.FilePath, r.MethodName, r.LineNumber })
                    .Select(g => new LocationHistoryResult
                    {
                        FilePath = g.Key.FilePath,
                        MethodName = g.Key.MethodName,
                        TotalFailuresAtLocation = g.Count(),
                        FirstFailure = g.Min(r => r.RecordedAt),
                        LastFailure = g.Max(r => r.RecordedAt),
                        AssociatedFailureSignatures = g.Select(r => r.FailureSignature).Distinct().ToList()
                    })
                    .Where(r => r.TotalFailuresAtLocation >= threshold)
                    .OrderByDescending(r => r.TotalFailuresAtLocation)
                    .ToList();

                return grouped;
            }
        });
    }

    private List<CodeLocationRecord> LoadStore()
    {
        try
        {
            var json = File.ReadAllText(_locationHistoryFilePath);
            return JsonSerializer.Deserialize<List<CodeLocationRecord>>(json) ?? new List<CodeLocationRecord>();
        }
        catch
        {
            return new List<CodeLocationRecord>();
        }
    }

    private void SaveStore(List<CodeLocationRecord> records)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var json = JsonSerializer.Serialize(records, options);
        File.WriteAllText(_locationHistoryFilePath, json);
    }
}