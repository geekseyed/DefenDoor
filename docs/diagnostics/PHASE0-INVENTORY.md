# Phase 0: Repository & Architecture Preflight Inventory

**Repository:** geekseyed/ISCM
**Branch:** epic/phase16-advanced-reporting-analytics
**Date:** 2026-09-16
**Baseline Commit:** df03d37

## 1. Build & Test Baseline

### Build Status: ✅ PASS
- **Errors:** 0
- **Warnings:** 0
- **Duration:** ~1.9s
- **Projects:**
  - ISCM.Domain (net8.0)
  - ISCM.Application (net8.0)
  - ISCM.Infrastructure (net8.0-windows)
  - ISCM.Tests (net8.0-windows)
  - ISCM.Web (net8.0-windows)

### Test Status: ✅ PASS
- **Total:** 252
- **Passed:** 252
- **Failed:** 0
- **Skipped:** 0
- **Duration:** ~24s
- **Observation:** Many "Unit" tests are actually **Real-System Integration Tests** that require Windows Admin privileges and hit real OS APIs (Registry, PowerShell, WMI). They are **not isolated** and will fail in restricted CI environments.

## 2. Environment

- **.NET SDK:** 9.0.200
- **.NET Runtime:** 8.0.13 (ASP.NET Core & NETCore.App)
- **OS:** Windows (Required for Infrastructure/Scanner projects)

## 3. Architecture Map

### Layers
- **Domain:** Entities (`ScanResult`, `Finding`), Value Objects, Enums.
- **Application:** Services (`ExecutiveKpiService`, `TrendAnalysisService`), Interfaces, Snapshots.
- **Infrastructure:** Persistence (SQLite), Reporting (QuestPDF, ClosedXML), Scanning (WindowsHardeningScanner).
- **Web:** Blazor Server (InteractiveServer), Background Services.
- **Tests:** xUnit, FluentAssertions (implied).

### Key State Containers (Scoped)
- `ScanStateService`: Holds current scan result & event log.
- `ReportGateService`: Manages report generation ledger.
- `ScanHistoryService`: In-memory + SQLite history.

### Key Background Services
- `ScheduledReportBackgroundService`: Polls every 30s.

## 4. Missing Configuration (Technical Debt)

- [ ] `.editorconfig` (Code style enforcement)
- [ ] `global.json` (SDK version pinning)
- [ ] `Directory.Build.props` (Centralized build properties)
- [ ] `NuGet.Config` (Package source configuration)

## 5. Findings Navigation Bug: Hypotheses

Since logic tests pass, the bug is likely in the **Blazor Server UI Layer**:

| ID | Hypothesis | Verification Method |
|----|------------|---------------------|
| H1 | **Circuit Freeze:** Unhandled exception in `Findings.razor` or `FindingDrawer.razor` kills the SignalR circuit. | Component Test (bUnit) + E2E Trace |
| H2 | **Sync-over-Async:** `.Result` or `.Wait()` blocking the UI thread during data load. | Static Analysis + Code Review |
| H3 | **State Mutation During Render:** Modifying `ScanStateService` inside `OnInitialized` without `InvokeAsync`. | Component Test |
| H4 | **Event Subscription Leak:** `OnChange` events not unsubscribed in `Dispose`, causing memory leaks or duplicate renders. | Code Review |
| H5 | **Navigation Lock:** A `NavigationLock` or similar component preventing navigation when `IsScanning` or similar flag is stuck. | E2E Test |

## 6. Test Gaps

- [ ] **Component Tests (bUnit):** Zero coverage for Blazor components.
- [ ] **E2E Tests (Playwright):** Zero coverage for browser workflows.
- [ ] **Isolated Unit Tests:** Current tests depend on OS state.

## 7. Next Steps

1.  **Phase 1:** Establish Static Analysis & Build Quality Gate (Add `.editorconfig`, `global.json`).
2.  **Phase 2:** Build Diagnostic Test Infrastructure (Isolated Test Host).
3.  **Phase 4:** Reproduce Findings Bug via Component Tests.