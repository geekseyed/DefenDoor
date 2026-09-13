namespace ISCM.Application.Interfaces;

using ISCM.Domain.ValueObjects;

/// <summary>
/// Phase 14.3: Abstraction for running external processes.
/// Base implementation executes Process.Start directly.
/// Can be decorated with caching, timeout, or logging layers.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Executes a command with arguments and returns the result.
    /// </summary>
    /// <param name="command">Executable name (e.g., "net", "auditpol")</param>
    /// <param name="arguments">Command arguments</param>
    /// <param name="timeout">Maximum execution time (null = 60 seconds default)</param>
    /// <returns>ProcessResult with output, exit code, and duration</returns>
    Task<ProcessResult> RunAsync(string command, string arguments, TimeSpan? timeout = null);
}