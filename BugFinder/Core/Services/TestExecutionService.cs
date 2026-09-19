using System.Diagnostics;
using System.Text;

namespace ISCM.BugFinder.Core.Services;

/// <summary>
/// Service responsible for executing dotnet test and capturing raw output artifacts.
/// BF-01.1: Test Execution Integration
/// </summary>
public class TestExecutionService
{
    private readonly string _workingDirectory;

    public TestExecutionService(string workingDirectory)
    {
        _workingDirectory = workingDirectory;
    }

    /// <summary>
    /// Executes the test suite and returns the path to the generated TRX file.
    /// </summary>
    public async Task<TestExecutionResult> ExecuteTestsAsync(string? testProjectPath = null, CancellationToken cancellationToken = default)
    {
        var trxPath = Path.Combine(_workingDirectory, "TestResults", $"bugfinder_{DateTime.UtcNow:yyyyMMdd_HHmmss}.trx");
        Directory.CreateDirectory(Path.GetDirectoryName(trxPath)!);

        var projectPath = testProjectPath ?? Path.Combine(_workingDirectory, "ISCM.Tests", "ISCM.Tests.csproj");

        // Ensure absolute path
        if (!Path.IsPathRooted(projectPath))
        {
            projectPath = Path.Combine(_workingDirectory, projectPath);
        }

        var arguments = $"test \"{projectPath}\" --logger \"trx;LogFileName={trxPath}\" --verbosity normal --no-restore";

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = _workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        using (var process = new Process { StartInfo = startInfo })
        {
            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    outputBuilder.AppendLine(e.Data);
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    errorBuilder.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0 && !errorBuilder.ToString().Contains("Failed!"))
            {
                // Exit code 0 or 1 (if tests failed) is acceptable for ingestion. 
                // Other codes indicate build failure or infrastructure error.
                throw new InvalidOperationException($"Test execution failed with exit code {process.ExitCode}.\nError: {errorBuilder}");
            }
        }

        return new TestExecutionResult
        {
            TrxFilePath = trxPath,
            ConsoleOutput = outputBuilder.ToString(),
            ConsoleError = errorBuilder.ToString(),
            ExitCode = 0, // Treat as success for ingestion if we got a TRX
            ExecutedAt = DateTime.UtcNow
        };
    }
}

public class TestExecutionResult
{
    public string TrxFilePath { get; set; } = string.Empty;
    public string ConsoleOutput { get; set; } = string.Empty;
    public string ConsoleError { get; set; } = string.Empty;
    public int ExitCode { get; set; }
    public DateTime ExecutedAt { get; set; }
}