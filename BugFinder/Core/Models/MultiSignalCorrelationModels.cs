using System;
using System.Collections.Generic;

namespace ISCM.BugFinder.Core.Models;

/// <summary>
/// BF-14.2: Multi-Signal Correlation Models
/// Aggregates the fused evidence package (BF-14.1) into per-signal
/// profiles, pairwise signal agreements, and correlated targets.
/// </summary>

/// <summary>Canonical investigation signals (stages 1-6) + static bridge.</summary>
public enum InvestigationSignal
{
    Test,        // TestFailure / DomainFailure / Sbfl / MultiTest
    Stack,       // BF-03 stack localization
    Coverage,    // BF-06 coverage
    Runtime,     // BF-08 runtime events
    Regression,  // BF-10 regression boundary
    Historical,  // BF-11 recurring failures
    Static       // BF-13.8 static<->dynamic bridge
}

/// <summary>One target supported by a signal.</summary>
public class SignalTargetSupport
{
    public string TargetKey { get; set; } = string.Empty;
    public double FusedStrength { get; set; }
}

/// <summary>Stages 1-6: per-signal profile.</summary>
public class SignalProfile
{
    public InvestigationSignal Signal { get; set; }
    public int TargetCount { get; set; }
    public List<SignalTargetSupport> Targets { get; set; } = new();
}

/// <summary>Stage 7: pairwise agreement between two signals.</summary>
public class SignalAgreement
{
    public InvestigationSignal SignalA { get; set; }
    public InvestigationSignal SignalB { get; set; }
    public int SharedTargetCount { get; set; }
    public List<string> SharedTargets { get; set; } = new();
}

/// <summary>Stage 7: one target seen through its supporting signals.</summary>
public class CorrelatedTarget
{
    public string TargetKey { get; set; } = string.Empty;
    public double FusedStrength { get; set; }
    public int SupportingSignalCount { get; set; }
    public List<InvestigationSignal> SupportingSignals { get; set; } = new();
}

/// <summary>Correlation report — input for BF-14.3 Investigation Chain.</summary>
public class MultiSignalCorrelationReport
{
    public List<SignalProfile> Profiles { get; set; } = new();
    public List<SignalAgreement> Agreements { get; set; } = new();
    public List<CorrelatedTarget> CorrelatedTargets { get; set; } = new();

    public int TotalTargets { get; set; }
    public int MultiSignalTargetCount { get; set; }   // supported by >= 2 signals
    public CorrelatedTarget? TopTarget { get; set; }  // most supporting signals
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
}