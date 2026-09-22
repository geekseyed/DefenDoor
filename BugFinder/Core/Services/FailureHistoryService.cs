using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ISCM.BugFinder.Core.Models;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// BF-11.1: Failure History Service
/// Manages persistence and retrieval of failure records to detect recurring issues.
/// Storage: JSON file in .bugfinder/history directory.
/// </summary>
public class FailureHistoryService
{
    private readonly string _historyFilePath;
    private readonly object _lockObj = new();

    public FailureHistoryService(string? workingDirectory = null)
    {
        // Determine base directory
        var baseDir = workingDirectory ?? Directory.GetCurrentDirectory();

        // Construct history directory path
        var historyDir = Path.Combine(baseDir, ".bugfinder", "history");

        // Ensure directory exists (BF-11.1 Requirement)
        if (!Directory.Exists(historyDir))
        {
            Directory.CreateDirectory(historyDir);
        }

        _historyFilePath = Path.Combine(historyDir, "failure_history.json");

        // Initialize file if missing to ensure valid JSON structure exists
        if (!File.Exists(_historyFilePath))
        {
            var emptyStore = new FailureHistoryStore();
            SaveStore(emptyStore);
        }
    }

    /// <summary>
    /// BF-11.1 - Stage 1: Record a new failure instance.
    /// </summary>
    public async Task RecordFailureAsync(FailureRecord record)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        if (string.IsNullOrEmpty(record.FailureSignature))
            throw new ArgumentException("FailureSignature is required.", nameof(record));

        await Task.Run(() =>
        {
            lock (_lockObj)
            {
                var store = LoadStore();

                // Add new record
                store.Records.Add(record);
                store.LastUpdated = DateTime.UtcNow;

                // Pruning strategy: Keep only the last 1000 records to prevent unbounded growth
                if (store.Records.Count > 1000)
                {
                    store.Records = store.Records
                        .OrderByDescending(r => r.OccurredAt)
                        .Take(1000)
                        .ToList();
                }

                SaveStore(store);
            }
        });
    }

    /// <summary>
    /// BF-11.1 - Stage 2: Retrieve history for a specific failure signature.
    /// </summary>
    public async Task<FailureHistoryResult> GetHistoryAsync(string failureSignature)
    {
        if (string.IsNullOrEmpty(failureSignature))
            return new FailureHistoryResult();

        return await Task.Run(() =>
        {
            lock (_lockObj)
            {
                var store = LoadStore();

                var matches = store.Records
                    .Where(r => r.FailureSignature == failureSignature)
                    .OrderByDescending(r => r.OccurredAt)
                    .ToList();

                if (matches.Count == 0)
                {
                    return new FailureHistoryResult
                    {
                        FailureSignature = failureSignature,
                        TotalOccurrences = 0,
                    };
                }

                // A failure is recurring if it has happened more than once
                var isRecurring = matches.Count > 1;

                return new FailureHistoryResult
                {
                    FailureSignature = failureSignature,
                    TotalOccurrences = matches.Count,
                    FirstSeen = matches.Min(r => r.OccurredAt),
                    LastSeen = matches.Max(r => r.OccurredAt),
                    RecentOccurrences = matches.Take(10).ToList()
                    // IsRecurring به صورت خودکار محاسبه می‌شود
                }; 
            }
        });
    }

    /// <summary>
    /// BF-11.3: Detect if a failure is recurring based on signature.
    /// </summary>
    public async Task<bool> IsRecurringFailureAsync(string failureSignature)
    {
        var history = await GetHistoryAsync(failureSignature);
        return history.IsRecurring;
    }

    /// <summary>
    /// Helper: Load store from disk with error handling.
    /// </summary>
    private FailureHistoryStore LoadStore()
    {
        try
        {
            if (!File.Exists(_historyFilePath))
                return new FailureHistoryStore();

            var json = File.ReadAllText(_historyFilePath);
            if (string.IsNullOrWhiteSpace(json))
                return new FailureHistoryStore();

            var store = JsonSerializer.Deserialize<FailureHistoryStore>(json);
            return store ?? new FailureHistoryStore();
        }
        catch (JsonException)
        {
            // If JSON is corrupted, start fresh
            return new FailureHistoryStore();
        }
        catch (IOException)
        {
            // If file is locked or missing, start fresh
            return new FailureHistoryStore();
        }
    }

    /// <summary>
    /// Helper: Save store to disk with proper JSON options.
    /// </summary>
    private void SaveStore(FailureHistoryStore store)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(store, options);
        File.WriteAllText(_historyFilePath, json);
    }
}