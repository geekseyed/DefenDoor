namespace ISCM.Domain.Enums;

/// <summary>
/// Phase 14.1: Permission types required for full scan functionality.
/// </summary>
public enum PermissionType
{
    /// <summary>
    /// Application is running with administrator privileges.
    /// Required for registry writes, service control, and security policy changes.
    /// </summary>
    Administrator,

    /// <summary>
    /// Application can read HKLM registry keys.
    /// Required for most hardening checks.
    /// </summary>
    RegistryRead,

    /// <summary>
    /// Application can execute secedit.exe for security policy analysis.
    /// </summary>
    SeceditExecution,

    /// <summary>
    /// Application can execute PowerShell scripts.
    /// </summary>
    PowerShellExecution
}