using ISCM.Application.Interfaces;
using ISCM.Domain.Entities;
using ISCM.Domain.Enums;
using ISCM.Domain.ValueObjects; // ← Added for Phase 15.1
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ISCM.Web.Services;

public class ScanStateService
{
    private readonly IServiceProvider? _serviceProvider;
    private ScanResult? _currentScanResult;
    private readonly List<string> _activityLog = new();
    private readonly List<string> _consoleLogs = new();

    public event Action? OnChange;

    // --- System Info Editing State ---
    public bool IsEditingSystemInfo { get; private set; }
    public string DraftHostname { get; set; } = "";
    public string DraftIpAddress { get; set; } = "";
    public string DraftOsVersion { get; set; } = "";
    public string DraftOsBuild { get; set; } = "";

    // --- Display State ---
    public string DisplayHostname { get; set; } = "Unknown";
    public string DisplayIpAddress { get; set; } = "0.0.0.0";
    public string DisplayOsVersion { get; set; } = "Unknown";
    public string DisplayOsBuild { get; set; } = "Unknown";

    // --- Scan State ---
    public bool IsScanning { get; private set; }
    public bool IsConnected { get; set; } = true;
    public int CompletedChecks { get; private set; }
    public int ExpectedChecks { get; private set; }
    public int TotalChecks => ExpectedChecks;
    public string ScanDuration { get; private set; } = "00:00";
    public string ScanTimeText => ScanDuration;

    // --- Live Stats ---
    public int LivePassCount { get; private set; }
    public int LiveFailCount { get; private set; }

    // --- Phase 15.1: Real-time Dashboard State ---
    public string? CurrentCheckId { get; private set; }
    public string? CurrentCheckName { get; private set; }

    // --- Console / Event Log ---
    public IReadOnlyList<string> ConsoleLogs => _consoleLogs.AsReadOnly();
    public IReadOnlyList<string> EventLog => _activityLog.AsReadOnly();
    public string ConsoleStatusText { get; private set; } = "Ready";
    public string ConsoleStatusClass { get; private set; } = "status-ready";

    // --- Context / Selection ---
    public string SelectedZone { get; set; } = "All";
    public string SelectedSystem { get; set; } = "All";
    public string HostnameContext { get; set; } = "Localhost";

    // --- Last Check Result ---
    public Finding? LastCheckResult { get; private set; }

    // --- Baseline Info (Phase 3.2) ---
    public string BaselineName { get; private set; } = "Hosseini Standard v1.0";

    public ScanStateService(IServiceProvider? serviceProvider = null)
    {
        _serviceProvider = serviceProvider;
    }

    public ScanResult? CurrentScanResult => _currentScanResult;
    public IReadOnlyList<string> ActivityLog => _activityLog.AsReadOnly();

    public void SetScanResult(ScanResult result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));

        // Phase 13.6: Deep-clone to prevent mutation of persisted snapshot data
        try
        {
            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
                WriteIndented = false
            });
            _currentScanResult = JsonSerializer.Deserialize<ScanResult>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? result;
        }
        catch
        {
            _currentScanResult = result;
        }

        UpdateDisplayInfo();

        if (!string.IsNullOrEmpty(result.BaselineId))
        {
            var baselineService = _serviceProvider?.GetService<IBaselineService>();
            if (baselineService != null)
            {
                var baseline = baselineService.GetBaselineById(result.BaselineId);
                if (baseline != null)
                {
                    BaselineName = $"{baseline.Name} v{baseline.Version}";
                }
            }
        }

        NotifyStateChanged();
    }

    public void ClearScanResult()
    {
        _currentScanResult = null;
        NotifyStateChanged();
    }

    public void LogAction(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        _activityLog.Add($"[{timestamp}] {message}");
        NotifyStateChanged();
    }

    public void LogConsole(string message, string statusClass = "status-info")
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        _consoleLogs.Add($"[{timestamp}] {message}");
        ConsoleStatusText = message;
        ConsoleStatusClass = statusClass;
        NotifyStateChanged();
    }

    private void UpdateDisplayInfo()
    {
        if (_currentScanResult != null)
        {
            DisplayHostname = _currentScanResult.Hostname ?? "Unknown";
            DisplayIpAddress = _currentScanResult.IpAddress ?? "0.0.0.0";
            DisplayOsVersion = _currentScanResult.OsVersion ?? "Unknown";
            DisplayOsBuild = _currentScanResult.OsBuild.ToString();
            HostnameContext = DisplayHostname;
        }
    }

    public void StartEditingSystemInfo()
    {
        if (_currentScanResult == null) return;
        IsEditingSystemInfo = true;
        DraftHostname = _currentScanResult.Hostname ?? "";
        DraftIpAddress = _currentScanResult.IpAddress ?? "";
        DraftOsVersion = _currentScanResult.OsVersion ?? "";
        DraftOsBuild = _currentScanResult.OsBuild.ToString();
        NotifyStateChanged();
    }

    public void SaveSystemInfoEdits()
    {
        if (_currentScanResult == null || !IsEditingSystemInfo) return;

        _currentScanResult.Hostname = DraftHostname;
        _currentScanResult.IpAddress = DraftIpAddress;
        _currentScanResult.OsVersion = DraftOsVersion;
        _currentScanResult.OsBuild = int.TryParse(DraftOsBuild, out var build) ? build : 0;

        UpdateDisplayInfo();
        IsEditingSystemInfo = false;
        LogAction("System information updated manually.");
        NotifyStateChanged();
    }

    public void CancelSystemInfoEdits()
    {
        IsEditingSystemInfo = false;
        NotifyStateChanged();
    }

    // Phase 15.1: Updated to use IProgress<ScanProgressUpdate>
    public async Task ExecuteScanAsync(ScanMode mode = ScanMode.Full)
    {
        if (IsScanning) return;

        IsScanning = true;
        CompletedChecks = 0;
        ExpectedChecks = 0;
        LivePassCount = 0;
        LiveFailCount = 0;
        CurrentCheckId = null;
        CurrentCheckName = null;
        _consoleLogs.Clear();
        _activityLog.Clear();

        LogConsole("Initializing scan...", "status-info");
        LogAction("Scan started.");

        try
        {
            var scanService = _serviceProvider?.GetService<IScanService>();
            if (scanService == null)
            {
                LogConsole("ERROR: IScanService not available", "status-error");
                return;
            }

            ExpectedChecks = scanService.TotalCheckCount;
            NotifyStateChanged();

            var progress = new Progress<ScanProgressUpdate>(update =>
            {
                LogConsole(update.LogMessage, update.LogMessage.Contains("ERROR") ? "status-error" : "status-info");

                CompletedChecks = update.CompletedChecks;
                ExpectedChecks = update.TotalChecks;
                LivePassCount = update.LivePassCount;
                LiveFailCount = update.LiveFailCount;
                CurrentCheckId = update.CurrentCheckId;
                CurrentCheckName = update.CurrentCheckName;

                NotifyStateChanged();
            });

            var result = await scanService.RunScanAsync(mode, progress);
            SetScanResult(result);
            LogConsole("Scan completed successfully.", "status-success");
            LogAction($"Scan completed: {result.PassCount} passed, {result.FailCount} failed.");
        }
        catch (Exception ex)
        {
            LogConsole($"Scan failed: {ex.Message}", "status-error");
        }
        finally
        {
            IsScanning = false;
            CurrentCheckId = null;
            CurrentCheckName = null;
            NotifyStateChanged();
        }
    }

    public async Task RescanCheckAsync(string checkId)
    {
        await RescanSingleCheckAsync(checkId);
    }

    public async Task RescanSingleCheckAsync(string checkId)
    {
        if (_currentScanResult == null) return;

        try
        {
            LogAction($"Rescanning {checkId}...");
            LogConsole($"Rescanning {checkId}...", "status-info");

            var scanService = _serviceProvider?.GetService<IScanService>();
            if (scanService == null)
            {
                LogAction("ERROR: IScanService not available");
                return;
            }

            var updatedFinding = await scanService.RescanCheckAsync(checkId);
            LastCheckResult = updatedFinding;

            if (_currentScanResult.Findings is List<Finding> findingsList)
            {
                var existingFinding = findingsList.FirstOrDefault(f => f.CheckId == checkId);
                if (existingFinding != null)
                {
                    findingsList.Remove(existingFinding);
                    findingsList.Add(updatedFinding);
                    LogAction($"{checkId} rescanned successfully");
                }
            }
            else
            {
                LogAction("WARNING: Findings collection is read-only. Cannot update in-place.");
            }

            NotifyStateChanged();
        }
        catch (Exception ex)
        {
            LogAction($"ERROR rescanning {checkId}: {ex.Message}");
        }
    }

    public async Task RescanSubControlAsync(string checkId, string subControlId)
    {
        if (_currentScanResult == null) return;

        try
        {
            LogAction($"Rescanning SubControl {subControlId}...");
            LogConsole($"Rescanning SubControl {subControlId}...", "status-info");

            var scanService = _serviceProvider?.GetService<IScanService>();
            if (scanService == null)
            {
                LogAction("ERROR: IScanService not available");
                return;
            }

            var updatedFinding = await scanService.RescanSubControlAsync(checkId, subControlId);
            LastCheckResult = updatedFinding;

            if (_currentScanResult.Findings is List<Finding> findingsList)
            {
                var existingFinding = findingsList.FirstOrDefault(f => f.CheckId == checkId);
                if (existingFinding != null)
                {
                    findingsList.Remove(existingFinding);
                    findingsList.Add(updatedFinding);
                    LogAction($"SubControl {subControlId} rescanned successfully");
                }
            }
            else
            {
                LogAction("WARNING: Findings collection is read-only. Cannot update in-place.");
            }

            NotifyStateChanged();
        }
        catch (Exception ex)
        {
            LogAction($"ERROR rescanning SubControl {subControlId}: {ex.Message}");
        }
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}