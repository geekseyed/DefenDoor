using ISCM.Domain.Common;
using ISCM.Domain.Enums;
using System;
using System.Collections.Generic;

namespace ISCM.Domain.Entities;

public class Finding : BaseEntity
{
    public string CheckId { get; private set; } = string.Empty;

    /// <summary>
    /// Optional SubControl identifier (e.g., "PWD-001.1", "PWD-001.2").
    /// </summary>
    public string? SubControlId { get; private set; }

    public string Name { get; private set; } = string.Empty;
    public CheckCategory Category { get; private set; }
    public CheckSeverity Severity { get; private set; }
    public CheckStatus Status { get; private set; }
    public string CurrentValue { get; private set; } = string.Empty;
    public string ExpectedValue { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string? RegistryPath { get; private set; }
    public string Recommendation { get; private set; } = string.Empty;
    public string? ErrorMessage { get; private set; }

    public string CisReference { get; private set; } = "";
    public int RiskScore { get; private set; } = 0;
    public string SourceType { get; private set; } = "";
    public string SourceCommand { get; private set; } = "";
    public IReadOnlyList<string> FixTools { get; private set; } = new List<string>();
    public IReadOnlyList<SubCheck> SubChecks { get; private set; } = new List<SubCheck>();
    public List<TestResult> TestResults { get; private set; } = new List<TestResult>();
    // Phase 15.2: Direct Evidence storage for Tree View
    public List<Evidence> Evidences { get; private set; } = new List<Evidence>();


    // Governance fields
    public bool IsSuppressed { get; private set; } = false;
    public string? IgnoreReason { get; private set; }
    public string? IgnoredBy { get; private set; }
    public DateTime? IgnoredAt { get; private set; }
    public bool IsFalsePositive { get; private set; } = false;

    private CheckStatus? _previousStatus;

    private Finding() { }

    public Finding(
        string checkId,
        string name,
        CheckCategory category,
        CheckSeverity severity,
        CheckStatus status,
        string currentValue,
        string expectedValue,
        string recommendation,
        string? errorMessage = null,
        string description = "",
        string? registryPath = null,
        string cisReference = "",
        int riskScore = 0,
        string sourceType = "",
        string sourceCommand = "",
        IReadOnlyList<string>? fixTools = null,
        IReadOnlyList<SubCheck>? subChecks = null,
        string? subControlId = null)
    {
        if (string.IsNullOrWhiteSpace(checkId))
            throw new ArgumentException("CheckId cannot be empty.", nameof(checkId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be empty.", nameof(name));

        CheckId = checkId;
        SubControlId = subControlId;
        Name = name;
        Category = category;
        Severity = severity;
        Status = status;
        CurrentValue = currentValue;
        ExpectedValue = expectedValue;
        Recommendation = recommendation;
        ErrorMessage = errorMessage;
        Description = description;
        RegistryPath = registryPath;
        CisReference = cisReference;
        RiskScore = riskScore;
        SourceType = sourceType;
        SourceCommand = sourceCommand;
        FixTools = fixTools ?? new List<string>();
        SubChecks = subChecks ?? new List<SubCheck>();
    }

    public void AddTestResult(TestResult result)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));

        TestResults.Add(result);
    }
    // Phase 15.2: Add Evidence for Tree View
    public void AddEvidence(Evidence evidence)
    {
        if (evidence == null)
            throw new ArgumentNullException(nameof(evidence));

        Evidences.Add(evidence);
    }

    /// <summary>
    /// Ignores this finding. Parameters are optional for backward compatibility with UI.
    /// </summary>
    public void Ignore(string reason = "Not specified", string ignoredBy = "System")
    {
        if (string.IsNullOrWhiteSpace(reason))
            reason = "Not specified";
        if (string.IsNullOrWhiteSpace(ignoredBy))
            ignoredBy = "System";

        IsSuppressed = true;
        IgnoreReason = reason;
        IgnoredBy = ignoredBy;
        IgnoredAt = DateTime.UtcNow;
        _previousStatus = Status;
        Status = CheckStatus.Ignored;
    }

    public void MarkFalsePositive()
    {
        IsFalsePositive = true;
        IsSuppressed = true;
        _previousStatus = Status;
        Status = CheckStatus.FalsePositive;
    }

    /// <summary>
    /// Reverts the Ignore or FalsePositive state.
    /// </summary>
    public void Undo()
    {
        if (!IsSuppressed)
            return;

        IsSuppressed = false;
        IsFalsePositive = false;
        IgnoreReason = null;
        IgnoredBy = null;
        IgnoredAt = null;

        if (_previousStatus.HasValue)
        {
            Status = _previousStatus.Value;
            _previousStatus = null;
        }
    }

    public void UpdateStatus(CheckStatus newStatus)
    {
        if (!IsSuppressed)
        {
            Status = newStatus;
        }
    }
}