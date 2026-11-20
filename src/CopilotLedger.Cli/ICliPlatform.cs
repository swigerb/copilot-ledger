namespace CopilotLedger.Cli;

public sealed record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError);

// What gh-ledger needs from the operating system beyond files in its data directory. Tests use a fake.
public interface ICliPlatform
{
    bool IsWindows { get; }
    bool IsMacOS { get; }
    string HomeDirectory { get; }
    string? ProcessPath { get; }                                           // Environment.ProcessPath
    string? ShellPath { get; }                                             // the SHELL environment variable
    string? GetEnvironmentVariable(string name);                           // current process
    string? GetUserEnvironmentVariable(string name);                       // Windows user scope; null on other platforms
    void SetUserEnvironmentVariable(string name, string? value);           // Windows user scope; null deletes; other platforms throw PlatformNotSupportedException
    bool FileExists(string path);
    string? FindOnPath(string fileName);                                   // full path of an executable on PATH, or null
    Task<ProcessRunResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
    int StartDetached(string fileName, IReadOnlyList<string> arguments, string workingDirectory);   // returns the new process id
    bool IsProcessRunning(int processId);
    void KillProcess(int processId);
}
