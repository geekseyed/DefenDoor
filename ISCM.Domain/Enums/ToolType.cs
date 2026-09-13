namespace ISCM.Domain.Enums;

/// <summary>
/// Phase 14.1: External tools required by certain checks.
/// </summary>
public enum ToolType
{
    /// <summary>
    /// powershell.exe - used by multiple checks and remediations.
    /// </summary>
    PowerShell,

    /// <summary>
    /// net.exe - used for account and group queries.
    /// </summary>
    NetExe,

    /// <summary>
    /// secedit.exe - used for security policy export/analysis.
    /// </summary>
    Secedit,

    /// <summary>
    /// auditpol.exe - used for audit policy queries.
    /// </summary>
    Auditpol
}