namespace ISCM.Domain.ValueObjects;

/// <summary>
/// Phase 14.3: Immutable result of a process execution.
/// </summary>
public record ProcessResult
{
    public required string Output { get; init; }
    public required string ErrorOutput { get; init; }
    public required int ExitCode { get; init; }
    public required int DurationMs { get; init; }
    public required DateTimeOffset ExecutedAt { get; init; }
    public required string Command { get; init; }
    public required string Arguments { get; init; }

    /// <summary>
    /// True if process exited with code 0.
    /// </summary>
    public bool Success => ExitCode == 0;

    /// <summary>
    /// Combined output (stdout + stderr if error).
    /// </summary>
    public string CombinedOutput => Success
        ? Output
        : $"{Output}\n[STDERR]: {ErrorOutput}";

    /// <summary>
    /// Returns the output if successful, otherwise returns error message.
    /// </summary>
    public string OutputOrError => Success ? Output : $"Error (exit {ExitCode}): {ErrorOutput}";
}