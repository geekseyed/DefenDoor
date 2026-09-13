namespace ISCM.Infrastructure.Scanning.Collectors;

using ISCM.Application.Interfaces;
using ISCM.Domain.Enums;
using ISCM.Domain.ValueObjects;
using Microsoft.Win32;
using System.Diagnostics;
using System.Security.Principal;

/// <summary>
/// Phase 14.1: Environment detection implementation.
/// Checks permissions, tool availability, and performance characteristics.
/// Results are cached for the lifetime of the service.
/// </summary>
public class EnvironmentDetector : IEnvironmentDetector
{
    private EnvironmentProfile? _cachedProfile;
    private readonly object _lock = new();

    public EnvironmentDetector()
    {
        // No dependencies needed - uses Windows APIs directly
    }

    public async Task<EnvironmentProfile> DetectEnvironmentAsync()
    {
        lock (_lock)
        {
            if (_cachedProfile != null)
                return _cachedProfile;
        }

        var permissions = await DetectPermissionsAsync();
        var tools = await DetectToolsAsync();
        var hardware = DetectHardware();
        var performance = DeterminePerformanceTier(hardware);
        var warnings = CollectWarnings(permissions, tools, hardware);

        var profile = new EnvironmentProfile
        {
            Permissions = permissions,
            AvailableTools = tools,
            PerformanceTier = performance,
            Warnings = warnings,
            DetectedAt = DateTimeOffset.UtcNow,
            Hardware = hardware
        };

        lock (_lock)
        {
            _cachedProfile = profile;
        }

        return profile;
    }

    public bool HasPermission(PermissionType permission)
    {
        var profile = GetCachedProfile();
        if (profile == null)
        {
            // Quick check without full detection
            return permission switch
            {
                PermissionType.Administrator => CheckIsAdmin(),
                PermissionType.RegistryRead => CheckCanReadRegistry(),
                PermissionType.SeceditExecution => CheckToolExists("secedit.exe"),
                PermissionType.PowerShellExecution => CheckToolExists("powershell.exe"),
                _ => false
            };
        }

        return permission switch
        {
            PermissionType.Administrator => profile.Permissions.IsAdmin,
            PermissionType.RegistryRead => profile.Permissions.CanReadRegistry,
            PermissionType.SeceditExecution => profile.Permissions.CanRunSecedit,
            PermissionType.PowerShellExecution => profile.Permissions.CanRunPowerShell,
            _ => false
        };
    }

    public bool IsToolAvailable(ToolType tool)
    {
        var profile = GetCachedProfile();
        if (profile == null)
        {
            // Quick check without full detection
            return tool switch
            {
                ToolType.PowerShell => CheckToolExists("powershell.exe"),
                ToolType.NetExe => CheckToolExists("net.exe"),
                ToolType.Secedit => CheckToolExists("secedit.exe"),
                ToolType.Auditpol => CheckToolExists("auditpol.exe"),
                _ => false
            };
        }

        return tool switch
        {
            ToolType.PowerShell => profile.AvailableTools.PowerShell,
            ToolType.NetExe => profile.AvailableTools.NetExe,
            ToolType.Secedit => profile.AvailableTools.Secedit,
            ToolType.Auditpol => profile.AvailableTools.Auditpol,
            _ => false
        };
    }

    public PerformanceTier GetPerformanceTier()
    {
        var profile = GetCachedProfile();
        if (profile == null)
        {
            var hardware = DetectHardware();
            return DeterminePerformanceTier(hardware);
        }

        return profile.PerformanceTier;
    }

    public EnvironmentProfile? GetCachedProfile()
    {
        lock (_lock)
        {
            return _cachedProfile;
        }
    }

    #region Permission Detection

    private async Task<PermissionProfile> DetectPermissionsAsync()
    {
        var isAdmin = CheckIsAdmin();
        var canReadRegistry = CheckCanReadRegistry();
        var canWriteRegistry = isAdmin ? CheckCanWriteRegistry() : false;
        var canRunSecedit = isAdmin && await CheckCanRunSeceditAsync();
        var canRunPowerShell = CheckToolExists("powershell.exe");

        return new PermissionProfile
        {
            IsAdmin = isAdmin,
            CanReadRegistry = canReadRegistry,
            CanWriteRegistry = canWriteRegistry,
            CanRunSecedit = canRunSecedit,
            CanRunPowerShell = canRunPowerShell
        };
    }

    private static bool CheckIsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static bool CheckCanReadRegistry()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return key != null && key.GetValue("ProductName") != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool CheckCanWriteRegistry()
    {
        try
        {
            // Try to create a temporary test key
            using var testKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\ISCM_TempTest");
            if (testKey == null)
                return false;

            Registry.LocalMachine.DeleteSubKey(@"SOFTWARE\ISCM_TempTest", false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> CheckCanRunSeceditAsync()
    {
        if (!CheckToolExists("secedit.exe"))
            return false;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "secedit.exe",
                Arguments = "/export /cfg \"$env:TEMP\\secedit_test.inf\" /quiet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return false;

            var task = process.WaitForExitAsync();
            if (await Task.WhenAny(task, Task.Delay(3000)) != task)
            {
                process.Kill();
                return false;
            }

            // Clean up temp file
            var tempPath = Path.Combine(Path.GetTempPath(), "secedit_test.inf");
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Tool Detection

    private async Task<ToolAvailabilityProfile> DetectToolsAsync()
    {
        var hasPowershell = CheckToolExists("powershell.exe");
        var psVersion = hasPowershell ? await GetPowerShellVersionAsync() : null;
        var hasNet = CheckToolExists("net.exe");
        var hasSecedit = CheckToolExists("secedit.exe");
        var hasAuditpol = CheckToolExists("auditpol.exe");

        return new ToolAvailabilityProfile
        {
            PowerShell = hasPowershell,
            PowerShellVersion = psVersion,
            NetExe = hasNet,
            Secedit = hasSecedit,
            Auditpol = hasAuditpol
        };
    }

    private static bool CheckToolExists(string toolName)
    {
        try
        {
            // Try to find in PATH
            var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(';') ?? Array.Empty<string>();
            foreach (var dir in pathDirs)
            {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;

                var fullPath = Path.Combine(dir, toolName);
                if (File.Exists(fullPath))
                    return true;
            }

            // Check common locations
            var system32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), toolName);
            if (File.Exists(system32))
                return true;

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string?> GetPowerShellVersionAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"$PSVersionTable.PSVersion.ToString()\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return null;

            var output = await process.StandardOutput.ReadToEndAsync();
            var task = process.WaitForExitAsync();

            if (await Task.WhenAny(task, Task.Delay(3000)) != task)
            {
                process.Kill();
                return null;
            }

            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Hardware Detection

    private static HardwareProfile DetectHardware()
    {
        var processorCount = Environment.ProcessorCount;
        var totalMemoryMB = GetTotalMemoryMB();
        var isVM = DetectIsVirtualMachine();
        var machineName = Environment.MachineName;

        return new HardwareProfile
        {
            ProcessorCount = processorCount,
            TotalMemoryMB = totalMemoryMB,
            IsVirtualMachine = isVM,
            MachineName = machineName
        };
    }

    private static long GetTotalMemoryMB()
    {
        try
        {
            var gcMemory = GC.GetGCMemoryInfo();
            return gcMemory.TotalAvailableMemoryBytes / (1024 * 1024);
        }
        catch
        {
            // Fallback to registry
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management");
                var totalPhysical = key?.GetValue("TotalPhysicalMemory");
                if (totalPhysical != null)
                {
                    return Convert.ToInt64(totalPhysical) / (1024 * 1024);
                }
            }
            catch { }

            return 0;
        }
    }

    private static bool DetectIsVirtualMachine()
    {
        try
        {
            // Check BIOS info
            using var biosKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            if (biosKey != null)
            {
                var systemManufacturer = biosKey.GetValue("SystemManufacturer")?.ToString() ?? "";
                var systemProductName = biosKey.GetValue("SystemProductName")?.ToString() ?? "";
                var biosVersion = biosKey.GetValue("BIOSVersion")?.ToString() ?? "";

                var vmIndicators = new[]
                {
                    "VMware", "VirtualBox", "Hyper-V", "QEMU", "KVM", "Xen",
                    "Parallels", "Virtual Machine", "VM"
                };

                foreach (var indicator in vmIndicators)
                {
                    if (systemManufacturer.Contains(indicator, StringComparison.OrdinalIgnoreCase) ||
                        systemProductName.Contains(indicator, StringComparison.OrdinalIgnoreCase) ||
                        biosVersion.Contains(indicator, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            // Check disk model
            using var diskKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Disk\Enum");
            if (diskKey != null)
            {
                var diskNames = diskKey.GetValueNames();
                foreach (var name in diskNames)
                {
                    var diskValue = diskKey.GetValue(name)?.ToString() ?? "";
                    if (diskValue.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                        diskValue.Contains("VMware", StringComparison.OrdinalIgnoreCase) ||
                        diskValue.Contains("QEMU", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Performance Tier Determination

    private static PerformanceTier DeterminePerformanceTier(HardwareProfile hardware)
    {
        // Slow tier conditions
        if (hardware.ProcessorCount < 2 || hardware.IsVirtualMachine)
            return PerformanceTier.Slow;

        // Medium tier conditions
        if (hardware.ProcessorCount < 4 || hardware.TotalMemoryMB < 4096)
            return PerformanceTier.Medium;

        // Fast tier
        return PerformanceTier.Fast;
    }

    #endregion

    #region Warning Collection

    private static IReadOnlyList<string> CollectWarnings(
        PermissionProfile permissions,
        ToolAvailabilityProfile tools,
        HardwareProfile hardware)
    {
        var warnings = new List<string>();

        if (!permissions.IsAdmin)
            warnings.Add("Not running as Administrator - registry writes and remediations will fail");

        if (!permissions.CanReadRegistry)
            warnings.Add("Cannot read HKLM registry - most checks will fail");

        if (!tools.PowerShell)
            warnings.Add("PowerShell not found - PowerShell-based checks will fail");

        if (!tools.NetExe)
            warnings.Add("net.exe not found - account-related checks will fail");

        if (!tools.Secedit)
            warnings.Add("secedit.exe not found - security policy checks will fail");

        if (hardware.IsVirtualMachine)
            warnings.Add("Virtual machine detected - some hardware checks may be unreliable");

        if (hardware.ProcessorCount < 2)
            warnings.Add($"Low CPU count ({hardware.ProcessorCount}) - scan will run sequentially");

        return warnings;
    }

    #endregion
}