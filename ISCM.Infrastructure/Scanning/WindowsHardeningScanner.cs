using ISCM.Application.Interfaces;
using ISCM.Application.Services;
using ISCM.Application.Services.Agreement;
using ISCM.Domain.Entities;
using ISCM.Domain.Enums;
using ISCM.Domain.ValueObjects; // ← Added for Phase 15.1
using ISCM.Infrastructure.Scanning.Collectors;
using System.Diagnostics;

namespace ISCM.Infrastructure.Scanning;

/// <summary>
/// Windows hardening scanner — orchestrates check execution, evidence collection,
/// normalization (Phase 6), evaluation, verification path tracking (Phase 8),
/// and typed pipeline (Phase 10).
///
/// Phase 11.5: Simplified to collector-only pattern.
/// Phase 12.11: Parallel execution via Parallel.ForEachAsync with configurable concurrency.
/// Phase 13.1: ScanId unified between ScanContext and ScanResult.
/// Phase 15.1: Upgraded to structured IProgress<ScanProgressUpdate> for real-time UI dashboard.
/// </summary>
public class WindowsHardeningScanner : IScanService
{
    private readonly WindowsSystemInfoCollector _systemInfoCollector;
    private readonly IEnumerable<IHardeningCheck> _checks;
    private readonly IControlEvaluator _controlEvaluator;
    private readonly IBaselineService _baselineService;
    private readonly IEvidenceAcquisitionService _acquisitionService;
    private readonly IScanFreshnessPolicy _freshnessPolicy;
    private readonly IFingerprintValidationService _fingerprintService;
    private readonly IScanInvalidationService _invalidationService;
    private readonly INormalizationService _normalizationService;
    private readonly VerificationPathService _verificationPathService;
    private readonly SubControlAggregationService _aggregationService;
    private readonly IScannerConfigurationService _configService;

    public WindowsHardeningScanner(
        WindowsSystemInfoCollector systemInfoCollector,
        IEnumerable<IHardeningCheck> checks,
        IControlEvaluator controlEvaluator,
        IBaselineService baselineService,
        IEvidenceAcquisitionService acquisitionService,
        IScanFreshnessPolicy freshnessPolicy,
        IFingerprintValidationService fingerprintService,
        IScanInvalidationService invalidationService,
        INormalizationService normalizationService,
        VerificationPathService verificationPathService,
        SubControlAggregationService aggregationService,
        IScannerConfigurationService configService)
    {
        _systemInfoCollector = systemInfoCollector ?? throw new ArgumentNullException(nameof(systemInfoCollector));
        _checks = checks ?? throw new ArgumentNullException(nameof(checks));
        _controlEvaluator = controlEvaluator ?? throw new ArgumentNullException(nameof(controlEvaluator));
        _baselineService = baselineService ?? throw new ArgumentNullException(nameof(baselineService));
        _acquisitionService = acquisitionService ?? throw new ArgumentNullException(nameof(acquisitionService));
        _freshnessPolicy = freshnessPolicy ?? throw new ArgumentNullException(nameof(freshnessPolicy));
        _fingerprintService = fingerprintService ?? throw new ArgumentNullException(nameof(fingerprintService));
        _invalidationService = invalidationService ?? throw new ArgumentNullException(nameof(invalidationService));
        _normalizationService = normalizationService ?? throw new ArgumentNullException(nameof(normalizationService));
        _verificationPathService = verificationPathService ?? throw new ArgumentNullException(nameof(verificationPathService));
        _aggregationService = aggregationService ?? throw new ArgumentNullException(nameof(aggregationService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
    }

    public int TotalCheckCount => _checks.Count();

    public async Task<ScanResult> RunScanAsync(ScanMode mode = ScanMode.Full, IProgress<ScanProgressUpdate>? progress = null)
    {
        // Phase 15.1: Thread-safe counters for parallel execution
        int completedChecks = 0;
        int livePassCount = 0;
        int liveFailCount = 0;
        var lockObj = new object();

        progress?.Report(new ScanProgressUpdate("[INFO] DefenDoor Scanner initialized", ScanProgressStage.Initializing, 0, TotalCheckCount, 0, 0));
        await Task.Delay(100);

        var defaultBaseline = _baselineService.GetDefaultBaseline();
        progress?.Report(new ScanProgressUpdate($"[INFO] Loading baseline: {defaultBaseline.Name} v{defaultBaseline.Version} ({TotalCheckCount} rules)", ScanProgressStage.Initializing, 0, TotalCheckCount, 0, 0));
        await Task.Delay(50);

        var (hostname, ipAddress, macAddress, osVersion, osBuild) = _systemInfoCollector.Collect();

        // Phase 4: Create new ScanContext with new ScanId
        var scanContext = new ScanContext(hostname, mode);

        // ═══════════════════════════════════════════════════════════
        // Phase 13.1 FIX: Inject scanContext.ScanId into ScanResult
        // ═══════════════════════════════════════════════════════════
        var scanResult = new ScanResult(
            hostname,
            ipAddress,
            macAddress,
            osVersion,
            osBuild,
            mode,
            targetId: hostname,
            scannerVersion: scanContext.ScannerVersion,
            scanId: scanContext.ScanId)
        {
            BaselineId = defaultBaseline.BaselineId
        };

        progress?.Report(new ScanProgressUpdate($"[INFO] Scan started: ScanId={scanContext.ScanId}", ScanProgressStage.CollectingSystemInfo, 0, TotalCheckCount, 0, 0));
        progress?.Report(new ScanProgressUpdate($"[INFO] Collecting system info... Hostname: {hostname}, IP: {ipAddress}", ScanProgressStage.CollectingSystemInfo, 0, TotalCheckCount, 0, 0));
        progress?.Report(new ScanProgressUpdate("[INFO] Collector: RegistryReader - reading HKLM policies...", ScanProgressStage.CollectingSystemInfo, 0, TotalCheckCount, 0, 0));
        await Task.Delay(100);

        // ═══════════════════════════════════════════════════════════
        // Phase 12.11: Parallel Execution
        // ═══════════════════════════════════════════════════════════
        var maxParallelism = _configService.GetMaxDegreeOfParallelism();
        progress?.Report(new ScanProgressUpdate($"[INFO] Parallel scan: MaxDegreeOfParallelism = {maxParallelism}", ScanProgressStage.ExecutingChecks, 0, TotalCheckCount, 0, 0));

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxParallelism
        };

        await Parallel.ForEachAsync(_checks, parallelOptions, async (check, cancellationToken) =>
        {
            // Phase 15.1: Report check start with current check info
            progress?.Report(new ScanProgressUpdate(
                $"[INFO] Executing {check.CheckId}: {check.Name}",
                ScanProgressStage.ExecutingChecks,
                completedChecks, TotalCheckCount, livePassCount, liveFailCount,
                check.CheckId, check.Name));

            await Task.Delay(50, cancellationToken);

            try
            {
                var controlDefinition = ControlCatalog.GetByCheckId(check.CheckId);
                if (controlDefinition == null)
                {
                    controlDefinition = new ControlDefinition
                    {
                        ControlId = check.CheckId,
                        Title = check.Name,
                        Category = check.Category,
                        Severity = check.Severity,
                        IsBaseline = true,
                        TechnicalCheckIds = new() { check.CheckId },
                        SubControls = new()
                    };
                }

                var collector = (IEvidenceCollector)check;
                var subControlResults = await RunCollectorOnlyPath(
                    collector, controlDefinition, scanContext, hostname, progress);

                var finding = _controlEvaluator.EvaluateFromSubControls(controlDefinition, subControlResults, check.CheckId);

                // Phase 15.1: Thread-safe update of counters and progress
                lock (lockObj)
                {
                    scanResult.AddFinding(finding);
                    completedChecks++;
                    if (finding.Status == CheckStatus.Pass) livePassCount++;
                    else if (finding.Status == CheckStatus.Fail) liveFailCount++;

                    progress?.Report(new ScanProgressUpdate(
                        BuildResultLine(finding),
                        ScanProgressStage.ExecutingChecks,
                        completedChecks, TotalCheckCount, livePassCount, liveFailCount,
                        finding.CheckId, finding.Name));
                }
            }
            catch (Exception ex)
            {
                var errorFinding = new Finding(
                    checkId: check.CheckId,
                    name: check.Name,
                    category: check.Category,
                    severity: check.Severity,
                    status: CheckStatus.Error,
                    currentValue: "Crash",
                    expectedValue: "N/A",
                    description: "The check failed to execute.",
                    errorMessage: ex.Message,
                    registryPath: string.Empty,
                    cisReference: null,
                    riskScore: (int)check.Severity * 20,
                    sourceType: "Error",
                    sourceCommand: string.Empty,
                    fixTools: new List<string>(),
                    subChecks: null,
                    recommendation: "Investigate the check execution error."
                );

                lock (lockObj)
                {
                    scanResult.AddFinding(errorFinding);
                    completedChecks++;
                    progress?.Report(new ScanProgressUpdate(
                        $"[ERROR] {check.CheckId}: {check.Name} = Crash ({ex.Message})",
                        ScanProgressStage.ExecutingChecks,
                        completedChecks, TotalCheckCount, livePassCount, liveFailCount,
                        check.CheckId, check.Name));
                }
            }
        });

        progress?.Report(new ScanProgressUpdate("[INFO] Finalizing scan and calculating compliance score...", ScanProgressStage.Finalizing, completedChecks, TotalCheckCount, livePassCount, liveFailCount));
        await Task.Delay(100);

        scanResult.CompleteScan();
        scanContext.MarkCompleted();

        progress?.Report(new ScanProgressUpdate("[INFO] Scan completed successfully.", ScanProgressStage.Completed, completedChecks, TotalCheckCount, livePassCount, liveFailCount));

        return scanResult;
    }

    /// <summary>
    /// Runs the collector-only path for IEvidenceCollector checks.
    /// </summary>
    private async Task<List<SubControlResult>> RunCollectorOnlyPath(
        IEvidenceCollector collector,
        ControlDefinition controlDefinition,
        ScanContext scanContext,
        string hostname,
        IProgress<ScanProgressUpdate>? progress) // ← Phase 15.1: Updated type
    {
        var checkId = collector.CollectorId;

        var evidenceList = await collector.CollectEvidenceAsync();

        if (evidenceList == null || evidenceList.Count == 0)
        {
            // Phase 15.1: Structured progress update
            progress?.Report(new ScanProgressUpdate(
                $"[WARNING] {checkId}: Collector returned no evidence",
                ScanProgressStage.ExecutingChecks, 0, 0, 0, 0));
            return new List<SubControlResult>();
        }

        var subControlResults = evidenceList
            .GroupBy(e => e.SubControlId ?? checkId)
            .Select(g =>
            {
                var subControlId = g.Key;
                var subControlDef = controlDefinition.SubControls
                    .FirstOrDefault(s => s.SubControlId == subControlId);

                var subResult = new SubControlResult
                {
                    SubControlId = subControlId,
                    Status = CheckStatus.NotScanned,
                    EvidenceItems = g.ToList(),
                    EvaluatedAt = DateTime.UtcNow
                };

                // Enrich evidence with scan context
                foreach (var evidence in subResult.EvidenceItems)
                {
                    if (string.IsNullOrEmpty(evidence.ScanId))
                        evidence.ScanId = scanContext.ScanId;
                    if (string.IsNullOrEmpty(evidence.ParentControlId))
                        evidence.ParentControlId = checkId;
                    if (string.IsNullOrEmpty(evidence.MachineIdentity))
                        evidence.MachineIdentity = hostname;

                    _fingerprintService.AssignFingerprint(evidence);

                    if (_freshnessPolicy.CanUseCachedEvidence(scanContext, evidence))
                    {
                        evidence.LifecycleState = EvidenceLifecycleState.Cached;
                    }
                    else
                    {
                        evidence.LifecycleState = EvidenceLifecycleState.Live;
                    }
                }

                if (subControlDef != null)
                {
                    var capabilityReport = _verificationPathService.ValidatePathCapability(subControlDef);
                    subResult.PathCapabilityReport = capabilityReport;

                    if (!capabilityReport.IsValid)
                    {
                        // Phase 15.1: Structured progress update
                        progress?.Report(new ScanProgressUpdate(
                            $"[WARNING] {subControlId}: Path capability issues: {string.Join(", ", capabilityReport.Errors)}",
                            ScanProgressStage.ExecutingChecks, 0, 0, 0, 0));
                    }
                }

                if (subControlDef != null && !string.IsNullOrWhiteSpace(subControlDef.ExpectedValue))
                {
                    try
                    {
                        _controlEvaluator.EvaluateSubControlTyped(
                            subResult,
                            subControlDef.ExpectedValue,
                            subControlDef.ExpectedValueType,
                            subControlDef.Operator
                        );
                    }
                    catch (Exception evalEx)
                    {
                        subResult.Status = CheckStatus.Error;
                        // Phase 15.1: Structured progress update
                        progress?.Report(new ScanProgressUpdate(
                            $"[ERROR] {subControlId}: Typed evaluation failed: {evalEx.Message}",
                            ScanProgressStage.ExecutingChecks, 0, 0, 0, 0));
                    }
                }
                else
                {
                    subResult.Status = CheckStatus.Unknown;
                }

                return subResult;
            })
            .ToList();

        return subControlResults;
    }

    public async Task<Finding> RescanCheckAsync(string checkId)
    {
        var check = _checks.FirstOrDefault(c => c.CheckId == checkId)
            ?? throw new InvalidOperationException($"Check '{checkId}' not found.");

        _invalidationService.InvalidateForRemediation("rescan", checkId);

        var scanContext = new ScanContext(checkId, ScanMode.Rescan);

        var controlDefinition = ControlCatalog.GetByCheckId(checkId);
        if (controlDefinition == null)
        {
            controlDefinition = new ControlDefinition
            {
                ControlId = checkId,
                Title = check.Name,
                Category = check.Category,
                Severity = check.Severity,
                IsBaseline = true,
                TechnicalCheckIds = new() { checkId },
                SubControls = new()
            };
        }

        var collector = (IEvidenceCollector)check;
        var subControlResults = await RunCollectorOnlyPath(
            collector, controlDefinition, scanContext, "rescan-host", null);

        return _controlEvaluator.EvaluateFromSubControls(controlDefinition, subControlResults, checkId);
    }

    public async Task<Finding> RescanSubControlAsync(string checkId, string subControlId)
    {
        var check = _checks.FirstOrDefault(c => c.CheckId == checkId)
            ?? throw new InvalidOperationException($"Check '{checkId}' not found.");

        Console.WriteLine($"[INFO] Rescan requested for SubControl {subControlId} within {checkId}");

        _invalidationService.InvalidateForRemediation("rescan", subControlId);

        return await RescanCheckAsync(checkId);
    }

    private static string BuildResultLine(Finding finding)
    {
        string tag = finding.Status switch
        {
            CheckStatus.Pass => "[PASS]",
            CheckStatus.Fail => "[FAIL]",
            CheckStatus.Unknown => "[UNKNOWN]",
            CheckStatus.Error => "[ERROR]",
            _ => "[INFO]"
        };

        string subControlInfo = finding.SubControlId != null ? $" [{finding.SubControlId}]" : "";
        string suffix = finding.Status == CheckStatus.Pass
            ? "(PASS)"
            : $"({finding.Status.ToString().ToUpper()} - expected {finding.ExpectedValue})";

        return $"{tag} {finding.CheckId}{subControlInfo}: {finding.Name} = {finding.CurrentValue} {suffix}";
    }
}