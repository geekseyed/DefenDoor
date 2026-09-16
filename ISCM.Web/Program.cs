using ISCM.Application.Evaluators;
using ISCM.Application.Evaluators.Typed;
using ISCM.Application.Interfaces;
using ISCM.Application.Normalizers;
using ISCM.Application.Parsers;
using ISCM.Application.Services;
using ISCM.Application.Services.Agreement;
using ISCM.Application.Validators;
using ISCM.Domain.Enums;
using ISCM.Domain.ValueObjects;
using ISCM.Infrastructure.Persistence.Mappers;
using ISCM.Infrastructure.Persistence.Repositories;
using ISCM.Infrastructure.Persistence;
using ISCM.Infrastructure.Reporting;
using Microsoft.EntityFrameworkCore;
using ISCM.Infrastructure.Scanning;
using ISCM.Infrastructure.Scanning.Checks;
using ISCM.Infrastructure.Scanning.Collectors;
using ISCM.Web.Components;
using ISCM.Web.Services;
using ISCM.Application.Snapshots;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<WindowsSystemInfoCollector>();

builder.Services.AddTransient<IHardeningCheck, FirewallDomainProfileCheck>();
builder.Services.AddTransient<IHardeningCheck, SmbV1ProtocolCheck>();
builder.Services.AddTransient<IHardeningCheck, AutoRunDisabledCheck>();
builder.Services.AddTransient<IHardeningCheck, WindowsDefenderCheck>();
builder.Services.AddTransient<IHardeningCheck, GuestAccountCheck>();
builder.Services.AddTransient<IHardeningCheck, UserAccountControlCheck>();
builder.Services.AddTransient<IHardeningCheck, UsbStorageCheck>();
builder.Services.AddTransient<IHardeningCheck, WindowsUpdateCheck>();
builder.Services.AddTransient<IHardeningCheck, AutoLogonCheck>();
builder.Services.AddTransient<IHardeningCheck, RdpNlaCheck>();
builder.Services.AddTransient<IHardeningCheck, AdminAccountCountCheck>();
builder.Services.AddTransient<IHardeningCheck, PasswordLengthCheck>();
builder.Services.AddTransient<IHardeningCheck, LmCompatibilityCheck>();
builder.Services.AddTransient<IHardeningCheck, ProcessCreationAuditingCheck>();
builder.Services.AddTransient<IHardeningCheck, PowerShellLoggingCheck>();
builder.Services.AddTransient<IHardeningCheck, DisableCmdCheck>();
builder.Services.AddTransient<IHardeningCheck, AccountLockoutCheck>();
builder.Services.AddTransient<IHardeningCheck, AdvancedAuditCheck>();
builder.Services.AddTransient<IHardeningCheck, UserRightsCheck>();
builder.Services.AddTransient<IHardeningCheck, LlmnrNetbiosCheck>();
builder.Services.AddTransient<IHardeningCheck, CredentialGuardCheck>();
builder.Services.AddTransient<IHardeningCheck, EventLogSizeCheck>();

// ═══════════════════════════════════════════════════════════
// Phase 2.5: Control Evaluator
// ═══════════════════════════════════════════════════════════
builder.Services.AddSingleton<IControlEvaluator>(sp =>
{
    var typedEvaluator = sp.GetRequiredService<ITypedEvidenceEvaluator>();
    return new ControlEvaluator(typedEvaluator);
});

// ═══════════════════════════════════════════════════════════
// Phase 3.3: Baseline Service
// ═══════════════════════════════════════════════════════════
builder.Services.AddSingleton<IBaselineService, BaselineService>();
builder.Services.AddSingleton<ICatalogValidator, CatalogValidator>();

// ═══════════════════════════════════════════════════════════
// Phase 4: Freshness & Cache Control
// ═══════════════════════════════════════════════════════════
builder.Services.AddSingleton<IEvidenceFingerprintGenerator, EvidenceFingerprintGenerator>();
builder.Services.AddSingleton<IEvidenceCacheService, EvidenceCacheService>();
builder.Services.AddSingleton<IEvidenceLifecycleService, EvidenceLifecycleService>();
builder.Services.AddSingleton<IScanFreshnessPolicy, ScanFreshnessPolicy>();
builder.Services.AddSingleton<IEvidenceAcquisitionService, EvidenceAcquisitionService>();
builder.Services.AddSingleton<IScanInvalidationService, ScanInvalidationService>();
builder.Services.AddSingleton<IRemediationVerificationService, RemediationVerificationService>();
builder.Services.AddSingleton<IScanFingerprintGenerator, ScanFingerprintGenerator>();
builder.Services.AddSingleton<IFingerprintValidationService, FingerprintValidationService>();
builder.Services.AddTransient<IScanContext, ScanContext>(sp =>
    new ScanContext("default", ScanMode.Full));

// ═══════════════════════════════════════════════════════════
// Phase 4.2: Remediation Service
// ═══════════════════════════════════════════════════════════
builder.Services.AddSingleton<IRemediationService, RemediationService>();

// ═══════════════════════════════════════════════════════════
// Phase 5: Parsers
// ═══════════════════════════════════════════════════════════
builder.Services.AddSingleton<RegistryParser>();
builder.Services.AddSingleton<SeceditParser>();
builder.Services.AddSingleton<NetAccountsParser>();
builder.Services.AddSingleton<AuditpolParser>();
builder.Services.AddSingleton<PowerShellParser>();
builder.Services.AddSingleton<IParserService, ParserService>();

builder.Services.AddSingleton<IParserRegistry>(sp =>
{
    var registry = new ParserRegistry();
    var registryParser = sp.GetRequiredService<RegistryParser>();
    registry.RegisterParser<string, RegistryValueData>(EvidenceSourceType.Registry, registryParser);
    registry.RegisterParser<string, RegistryValueData>(EvidenceSourceType.Other, registryParser);

    var seceditParser = sp.GetRequiredService<SeceditParser>();
    registry.RegisterParser<string, SeceditPolicyData>(EvidenceSourceType.Secedit, seceditParser);

    var netAccountsParser = sp.GetRequiredService<NetAccountsParser>();
    registry.RegisterParser<string, NetAccountsData>(EvidenceSourceType.NetAccounts, netAccountsParser);

    var auditpolParser = sp.GetRequiredService<AuditpolParser>();
    registry.RegisterParser<string, AuditpolData>(EvidenceSourceType.Auditpol, auditpolParser);

    var powerShellParser = sp.GetRequiredService<PowerShellParser>();
    registry.RegisterParser<string, PowerShellData>(EvidenceSourceType.PowerShell, powerShellParser);
    registry.RegisterParser<string, PowerShellData>(EvidenceSourceType.Other, powerShellParser);

    return registry;
});

// ═══════════════════════════════════════════════════════════
// Phase 6: Normalizers
// ═══════════════════════════════════════════════════════════
builder.Services.AddSingleton<RegistryNormalizer>();
builder.Services.AddSingleton<SeceditNormalizer>();
builder.Services.AddSingleton<NetAccountsNormalizer>();
builder.Services.AddSingleton<AuditpolNormalizer>();
builder.Services.AddSingleton<PowerShellNormalizer>();

builder.Services.AddSingleton<INormalizerRegistry>(sp =>
{
    var registry = new NormalizerRegistry();
    registry.RegisterNormalizer<RegistryValueData>(EvidenceSourceType.Registry, sp.GetRequiredService<RegistryNormalizer>());
    registry.RegisterNormalizer<SeceditPolicyData>(EvidenceSourceType.Secedit, sp.GetRequiredService<SeceditNormalizer>());
    registry.RegisterNormalizer<NetAccountsData>(EvidenceSourceType.NetAccounts, sp.GetRequiredService<NetAccountsNormalizer>());
    registry.RegisterNormalizer<AuditpolData>(EvidenceSourceType.Auditpol, sp.GetRequiredService<AuditpolNormalizer>());
    registry.RegisterNormalizer<PowerShellData>(EvidenceSourceType.PowerShell, sp.GetRequiredService<PowerShellNormalizer>());
    registry.RegisterNormalizer<PowerShellData>(EvidenceSourceType.Other, sp.GetRequiredService<PowerShellNormalizer>());
    return registry;
});

builder.Services.AddSingleton<INormalizationService, NormalizationService>();

// ═══════════════════════════════════════════════════════════
// Phase 7: Typed Evaluation
// ═══════════════════════════════════════════════════════════
builder.Services.AddSingleton<ExpectedValueParser>();
builder.Services.AddSingleton<IntegerEvaluator>();
builder.Services.AddSingleton<LongEvaluator>();
builder.Services.AddSingleton<BooleanEvaluator>();
builder.Services.AddSingleton<StringEvaluator>();
builder.Services.AddSingleton<DurationEvaluator>();
builder.Services.AddSingleton<SizeEvaluator>();
builder.Services.AddSingleton<EnumEvaluator>();
builder.Services.AddSingleton<CollectionEvaluator>();
builder.Services.AddSingleton<RegistryValueEvaluator>();
builder.Services.AddSingleton<PolicyValueEvaluator>();
builder.Services.AddSingleton<ITypedEvidenceEvaluator, TypedEvidenceEvaluator>();

// ═══════════════════════════════════════════════════════════
// Phase 8: Verification Architecture
// ═══════════════════════════════════════════════════════════
builder.Services.AddSingleton<VerificationPathService>();

// ═══════════════════════════════════════════════════════════
// Phase 9: Agreement Engine
// ═══════════════════════════════════════════════════════════
builder.Services.AddSingleton<IAgreementPolicy, DefaultAgreementPolicy>();
builder.Services.AddSingleton<SubControlAggregationService>();

// ═══════════════════════════════════════════════════════════
// Phase 13.5: Persistence & Snapshot Infrastructure
// ═══════════════════════════════════════════════════════════
builder.Services.AddSingleton<IStoragePathProvider, SqliteStoragePathProvider>();

builder.Services.AddDbContext<DefenDoorDbContext>((sp, options) =>
{
    var pathProvider = sp.GetRequiredService<IStoragePathProvider>();
    var dbPath = pathProvider.GetDatabasePath();
    options.UseSqlite($"Data Source={dbPath}");
}, ServiceLifetime.Scoped);

builder.Services.AddSingleton<ISnapshotMapper, ScanResultToSnapshotMapper>();
builder.Services.AddScoped<ISnapshotRepository, SqliteSnapshotRepository>();
builder.Services.AddSingleton<ISnapshotDiffEngine, SnapshotDiffEngine>();

// ═══════════════════════════════════════════════════════════
// Phase 13.6: Decorator Pattern for Persistence
// ═══════════════════════════════════════════════════════════
builder.Services.AddScoped<WindowsHardeningScanner>();
builder.Services.AddScoped<IScanService>(sp =>
{
    var inner = sp.GetRequiredService<WindowsHardeningScanner>();
    var repository = sp.GetRequiredService<ISnapshotRepository>();
    var mapper = sp.GetRequiredService<ISnapshotMapper>();
    return new PersistentScanService(inner, repository, mapper);
});

// ═══════════════════════════════════════════════════════════
// Phase 14.1-14.4: Advanced Scanning Features
// ═══════════════════════════════════════════════════════════
builder.Services.AddSingleton<IEnvironmentDetector, EnvironmentDetector>();
builder.Services.AddSingleton<IAdaptiveExecutionEngine, AdaptiveExecutionEngine>();

builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
builder.Services.AddSingleton<IProcessCacheService>(sp =>
{
    var runner = sp.GetRequiredService<IProcessRunner>();
    var config = sp.GetRequiredService<IScannerConfigurationService>();
    return new CachedProcessRunner(runner, config);
});

builder.Services.AddSingleton<IScannerConfigurationService>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    return new ScannerConfigurationService(configuration);
});

// ═══════════════════════════════════════════════════════════
// Phase 16.1-16.5: Reporting & Analytics
// ═══════════════════════════════════════════════════════════
builder.Services.AddScoped<IReportService, HtmlReportGenerator>();
builder.Services.AddScoped<IReportTemplateService, ReportTemplateService>();
builder.Services.AddScoped<IScheduledReportService, ScheduledReportService>();
builder.Services.AddScoped<ITrendAnalysisService, TrendAnalysisService>();
builder.Services.AddScoped<IExecutiveKpiService, ExecutiveKpiService>();

builder.Services.AddHostedService<ScheduledReportBackgroundService>();

// ═══════════════════════════════════════════════════════════
// Web Services
// ═══════════════════════════════════════════════════════════
builder.Services.AddScoped<ScanStateService>(sp => new ScanStateService(sp));
builder.Services.AddScoped<ScanHistoryService>();
builder.Services.AddScoped<ThemeService>();
builder.Services.AddScoped<ReportGateService>();

var app = builder.Build();

// ═══════════════════════════════════════════════════════════
// Post-Build Initialization
// ═══════════════════════════════════════════════════════════
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<DefenDoorDbContext>();
    dbContext.Database.EnsureCreated();
}

using (var scope = app.Services.CreateScope())
{
    var validator = scope.ServiceProvider.GetRequiredService<ICatalogValidator>();
    var result = validator.ValidateCatalog();
    Console.WriteLine($"[INFO] Catalog Integrity Validation: {(result.IsValid ? "PASSED" : $"FAILED ({result.CriticalIssues} critical, {result.HighIssues} high issues)")}");
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();