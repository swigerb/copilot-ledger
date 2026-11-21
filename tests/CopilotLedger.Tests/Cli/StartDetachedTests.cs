using System.ComponentModel;
using System.Diagnostics;
using CopilotLedger.Cli;
using CopilotLedger.Tests.TestSupport;

namespace CopilotLedger.Tests.Cli;

public sealed class StartDetachedTests
{
    // On Windows, CreateProcess fails at once for a missing file. Elsewhere, /bin/sh reports a file it can't execute on
    // the caller's standard error, which the test redirects to read it. The backslash in the file name checks that the
    // path is printed as is: dash's echo would stop at "\c".
    [Fact]
    public async Task SD1_StartDetachedFailsLoudlyWhenTheFileCantRun()
    {
        using TempDirectory temp = new();
        if (OperatingSystem.IsWindows())
        {
            Win32Exception exception = Assert.Throws<Win32Exception>(() => new SystemCliPlatform().StartDetached(
                Path.Combine(temp.Path, "gh-ledger.exe"),
                ["daemon", "run"],
                temp.Path));
            Assert.Equal(2, exception.NativeErrorCode);
            return;
        }

        string file = Path.Combine(temp.Path, @"gh-ledger\cut");
        await File.WriteAllTextAsync(file, "#!/bin/sh\n");
        ProcessStartInfo startInfo = SystemCliPlatform.UnixDetachedStartInfo(file, ["daemon", "run"], temp.Path);
        startInfo.RedirectStandardError = true;
        using Process process = Process.Start(startInfo)!;
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.Equal(126, process.ExitCode);
        Assert.Equal($"gh-ledger: cannot execute {file}\n", error);
    }
}
