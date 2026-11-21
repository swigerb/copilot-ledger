using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace CopilotLedger.Cli;

public sealed class SystemCliPlatform : ICliPlatform
{
    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsMacOS => OperatingSystem.IsMacOS();
    public string HomeDirectory =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string? ProcessPath => Environment.ProcessPath;
    public string? ShellPath => Environment.GetEnvironmentVariable("SHELL");

    public string? GetEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name);

    public string? GetUserEnvironmentVariable(string name) =>
        IsWindows ? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User) : null;

    public void SetUserEnvironmentVariable(string name, string? value)
    {
        if (!IsWindows)
        {
            throw new PlatformNotSupportedException();
        }

        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
    }

    public string? GetStartupCommand(string name)
    {
        if (OperatingSystem.IsWindows())
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                writable: false);
            return key?.GetValue(name) as string;
        }

        return null;
    }

    public void SetStartupCommand(string name, string? command)
    {
        if (OperatingSystem.IsWindows())
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                writable: true);
            if (command is null)
            {
                key.DeleteValue(name, throwOnMissingValue: false);
            }
            else
            {
                key.SetValue(name, command, RegistryValueKind.String);
            }

            return;
        }

        throw new PlatformNotSupportedException();
    }

    public bool FileExists(string path) => File.Exists(path);

    public string? FindOnPath(string fileName)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (path is null)
        {
            return null;
        }

        string candidateName = IsWindows
            && Path.GetExtension(fileName).Length == 0
            ? fileName + ".exe"
            : fileName;
        foreach (string entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(entry, candidateName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public async Task<ProcessRunResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using Process process = new();
        process.StartInfo.FileName = fileName;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
        process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new ProcessRunResult(process.ExitCode, await output, await error);
    }

    public int StartDetached(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsDetachedProcess.Start(fileName, arguments, workingDirectory);
        }

        using Process process = new() { StartInfo = UnixDetachedStartInfo(fileName, arguments, workingDirectory) };
        process.Start();
        return process.Id;
    }

    // /bin/sh first checks that fileName can be executed: if not, it says so on the caller's standard error and exits
    // with 126, because a failed exec would report to the null device. Otherwise exec keeps the process id, and the
    // redirections replace the inherited standard handles before the daemon runs. printf, not echo: dash's echo
    // interprets backslashes in the path.
    public static ProcessStartInfo UnixDetachedStartInfo(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        ProcessStartInfo startInfo = new("/bin/sh")
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(
            """[ -x "$0" ] || { printf 'gh-ledger: cannot execute %s\n' "$0" >&2; exit 126; }; exec "$0" "$@" </dev/null >/dev/null 2>&1""");
        startInfo.ArgumentList.Add(fileName);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    public bool IsProcessRunning(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    public void KillProcess(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            process.Kill();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
        }
    }
}
