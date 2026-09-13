namespace ISCM.Infrastructure.Scanning;

using ISCM.Application.Interfaces;
using ISCM.Domain.ValueObjects;
using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading.Tasks;

/// <summary>
/// Phase 14.3: Base implementation of IProcessRunner.
/// Executes Process.Start directly without caching.
///
/// Features:
/// - Asynchronous stdout/stderr reading
/// - Timeout support (default 60 seconds)
/// - Graceful process termination on timeout
/// - No exception throwing (returns error in ProcessResult)
/// </summary>
[SupportedOSPlatform("windows")]
public class ProcessRunner : IProcessRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    public async Task<ProcessResult> RunAsync(
        string command,
        string arguments,
        TimeSpan? timeout = null)
    {
        var startTime = DateTime.UtcNow;
        var effectiveTimeout = timeout ?? DefaultTimeout;

        try
        {
            var psi = new ProcessStartInfo(command, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return CreateErrorResult(command, arguments,
                    "Process.Start returned null", startTime);
            }

            // Read output asynchronously to prevent deadlocks
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var exitTask = process.WaitForExitAsync();

            // Wait for all tasks with timeout
            var allTasks = Task.WhenAll(outputTask, errorTask, exitTask);
            var completedTask = await Task.WhenAny(allTasks, Task.Delay(effectiveTimeout));

            if (completedTask != allTasks)
            {
                // Timeout occurred
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Ignore kill errors
                }

                return CreateErrorResult(command, arguments,
                    $"Process timed out after {effectiveTimeout.TotalSeconds:F1}s",
                    startTime);
            }

            var duration = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;

            return new ProcessResult
            {
                Output = await outputTask ?? string.Empty,
                ErrorOutput = await errorTask ?? string.Empty,
                ExitCode = process.ExitCode,
                DurationMs = duration,
                ExecutedAt = DateTimeOffset.UtcNow,
                Command = command,
                Arguments = arguments
            };
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // Command not found or access denied
            return CreateErrorResult(command, arguments,
                $"Command not found or access denied: {ex.Message}", startTime);
        }
        catch (Exception ex)
        {
            return CreateErrorResult(command, arguments, ex.Message, startTime);
        }
    }

    private static ProcessResult CreateErrorResult(
        string command, string arguments, string error, DateTime startTime)
    {
        return new ProcessResult
        {
            Output = string.Empty,
            ErrorOutput = error,
            ExitCode = -1,
            DurationMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds,
            ExecutedAt = DateTimeOffset.UtcNow,
            Command = command,
            Arguments = arguments
        };
    }
}