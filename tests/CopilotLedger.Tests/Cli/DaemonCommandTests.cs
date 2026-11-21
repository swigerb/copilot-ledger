using System.Text.Json;
using CopilotLedger.Tests.TestSupport;

namespace CopilotLedger.Tests.Cli;

public sealed class DaemonCommandTests
{
    [Fact]
    public async Task DM1_RunningDaemonIsExclusiveAndStopsByMarker()
    {
        using var temp = new TempDirectory();
        await using var harness = new CliHarness(temp.Path);
        await harness.StartInProcessDaemonAsync();
        string statePath = Path.Combine(temp.Path, "data", "daemon.json");
        using JsonDocument state = JsonDocument.Parse(await FileRetry.ReadAllTextAsync(statePath));
        JsonElement root = state.RootElement;
        Assert.Equal(Environment.ProcessId, root.GetProperty("processId").GetInt32());
        Assert.Equal("http://localhost:4318", root.GetProperty("endpoint").GetString());
        Assert.Equal("0.3.0", root.GetProperty("version").GetString());
        Assert.Equal(
            Path.Combine(temp.Path, "data", "ledger.db"),
            root.GetProperty("databasePath").GetString());

        CliRun start = await harness.RunAsync("daemon", "start");
        Assert.Equal(0, start.ExitCode);
        Assert.Equal(
            $"gh-ledger daemon is already running (pid {Environment.ProcessId}).\n",
            start.Out);
        CliRun duplicate = await harness.RunAsync("daemon", "run");
        Assert.Equal(1, duplicate.ExitCode);
        Assert.Contains("gh-ledger daemon is already running.", duplicate.Out);
        Assert.True(File.Exists(statePath));
        using JsonDocument stateAfterDuplicate = JsonDocument.Parse(
            await FileRetry.ReadAllTextAsync(statePath));
        Assert.Equal(
            Environment.ProcessId,
            stateAfterDuplicate.RootElement.GetProperty("processId").GetInt32());

        CliRun stop = await harness.RunAsync("daemon", "stop");
        Assert.Equal(0, stop.ExitCode);
        Assert.Equal("gh-ledger daemon stopped.\n", stop.Out);
        CliRun daemon = await harness.InProcessDaemon!;
        Assert.Equal(0, daemon.ExitCode);
        Assert.False(File.Exists(statePath));
        Assert.False(File.Exists(Path.Combine(temp.Path, "data", "daemon.stop")));
        CliRun stoppedAgain = await harness.RunAsync("daemon", "stop");
        Assert.Equal(0, stoppedAgain.ExitCode);
        Assert.Equal("gh-ledger daemon is not running.\n", stoppedAgain.Out);
    }

    [Fact]
    public async Task DM2_StartLaunchesTheDetachedCommand()
    {
        using var temp = new TempDirectory();
        await using var harness = new CliHarness(temp.Path);
        CliRun run = await harness.RunAsync("daemon", "start");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            $"gh-ledger daemon started (pid {Environment.ProcessId}) on http://localhost:4318.\n",
            run.Out);
        DetachedStart detached = Assert.Single(harness.Platform.DetachedStarts);
        Assert.Equal("/opt/gh-ledger/gh-ledger", detached.FileName);
        Assert.Equal(
            [
                "daemon",
                "run",
                "--port",
                "4318",
                "--log-file",
                Path.Combine(temp.Path, "data", "daemon.log"),
            ],
            detached.Arguments);
        Assert.Equal(Path.Combine(temp.Path, "data"), detached.WorkingDirectory);
    }

    [Fact]
    public async Task DM3_StartFailsWhenDetachedProcessExits()
    {
        using var temp = new TempDirectory();
        FakeCliPlatform platform = new()
        {
            StartDetachedHandler = (_, _, _) => 999999,
        };
        await using var harness = new CliHarness(temp.Path, platform);
        CliRun run = await harness.RunAsync("daemon", "start");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(
            $"gh-ledger daemon did not start; see {Path.Combine(temp.Path, "data", "daemon.log")}.\n",
            run.Error);
    }

    public static TheoryData<string, int> ProcessPaths => new()
    {
        { "/usr/bin/dotnet", 1 },
        { @"C:\tools\GH-LEDGER.EXE", 0 },
        { "/opt/bin/gh-ledger", 0 },
    };

    [Theory]
    [MemberData(nameof(ProcessPaths))]
    public async Task DM4_StartRequiresTheGhLedgerExecutable(
        string processPath,
        int expectedExit)
    {
        using var temp = new TempDirectory();
        FakeCliPlatform platform = new() { ProcessPath = processPath };
        await using var harness = new CliHarness(temp.Path, platform);
        CliRun run = await harness.RunAsync("daemon", "start");

        Assert.Equal(expectedExit, run.ExitCode);
        if (expectedExit == 1)
        {
            Assert.Equal(
                $"Run this command from the gh-ledger executable (current process: {processPath}).\n",
                run.Error);
        }
    }

    [Fact]
    public async Task DM5_InvalidOverlayDoesNotLeaveDaemonStateOrLock()
    {
        using var temp = new TempDirectory();
        await using var harness = new CliHarness(temp.Path);
        string data = Path.Combine(temp.Path, "data");
        Directory.CreateDirectory(data);
        string malformed = "{ \"schemaVersion\": 1,\n  \"foundryByok\": { \"models\": [ }\n}\n";
        await File.WriteAllTextAsync(Path.Combine(data, "rates.json"), malformed);

        CliRun run = await harness.RunAsync("daemon", "run");
        Assert.Equal(1, run.ExitCode);
        Assert.Contains(
            $"Invalid rates overlay: {Path.Combine(data, "rates.json")}: line 2, column 32: invalid JSON",
            run.Out);
        Assert.DoesNotContain("gh-ledger daemon stopped", run.Out);
        Assert.False(File.Exists(Path.Combine(data, "daemon.json")));
        CliRun stop = await harness.RunAsync("daemon", "stop");
        Assert.Equal(0, stop.ExitCode);
        Assert.Equal("gh-ledger daemon is not running.\n", stop.Out);
    }

    [Fact]
    public async Task DM6_LockedStopFileIsDeletedOnceReleased()
    {
        using var temp = new TempDirectory();
        await using var harness = new CliHarness(temp.Path);
        await harness.StartInProcessDaemonAsync();
        string stopPath = Path.Combine(temp.Path, "data", "daemon.stop");
        string statePath = Path.Combine(temp.Path, "data", "daemon.json");

        // Held without delete sharing, the stop file can't be deleted on Windows until it's released.
        using (FileStream held = new(stopPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        CliRun daemon = await harness.InProcessDaemon!.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, daemon.ExitCode);
        Assert.False(File.Exists(stopPath));
        Assert.False(File.Exists(statePath));
    }
}
