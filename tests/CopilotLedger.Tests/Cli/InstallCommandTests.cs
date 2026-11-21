using System.Text.Json;
using CopilotLedger.Cli;
using CopilotLedger.Tests.TestSupport;

namespace CopilotLedger.Tests.Cli;

public sealed class InstallCommandTests
{
    [Fact]
    public async Task IN1_FreshWindowsInstallMakesOnlyFakePlatformChanges()
    {
        using TempDirectory temp = new();
        FakeCliPlatform platform = WindowsPlatform();
        await using CliHarness harness = new(temp.Path, platform);

        CliRun run = await harness.RunAsync("install");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [("COPILOT_OTEL_ENABLED", "true")],
            platform.UserVariableWrites);
        (string gh, IReadOnlyList<string> arguments) =
            Assert.Single(platform.ProcessRuns);
        Assert.Equal(@"C:\gh\gh.exe", gh);
        Assert.Equal(
            [
                "alias",
                "set",
                "ledger",
                "'C:/tools/gh-ledger.exe' \"$@\"",
                "--shell",
                "--clobber",
            ],
            arguments);
        Assert.Equal(
            Normalize($"""
            Set COPILOT_OTEL_ENABLED=true for your user account.
            Set gh alias: gh ledger runs C:\tools\gh-ledger.exe.
            Warning: GitHub CLI needs sh.exe from Git for Windows to run shell aliases, so gh ledger won't work until Git for Windows is installed. Run gh-ledger directly.
            gh-ledger daemon started (pid {Environment.ProcessId}) on http://localhost:4318.
            CopilotLedger is installed. Restart the GitHub Copilot app, VS Code, and open terminals so they pick up COPILOT_OTEL_ENABLED.

            """),
            run.Out);
        using JsonDocument manifest = JsonDocument.Parse(
            await File.ReadAllTextAsync(ManifestPath(temp.Path)));
        Assert.Equal(
            @"C:\tools\gh-ledger.exe",
            manifest.RootElement.GetProperty("executablePath").GetString());
        Assert.True(
            manifest.RootElement.GetProperty("windowsUserVariableSet").GetBoolean());
        Assert.Equal(
            JsonValueKind.Null,
            manifest.RootElement
                .GetProperty("windowsUserVariablePreviousValue").ValueKind);
        Assert.Empty(
            manifest.RootElement.GetProperty("profileFiles").EnumerateArray());
        Assert.True(manifest.RootElement.GetProperty("ghAliasSet").GetBoolean());
    }

    [Fact]
    public async Task IN2_WindowsInstallOmitsShWarningWhenGitForWindowsProvidesSh()
    {
        using TempDirectory temp = new();
        FakeCliPlatform platform = WindowsPlatform();
        platform.ExistingFiles.Add(@"C:\git\cmd\..\bin\sh.exe");
        await using CliHarness harness = new(temp.Path, platform);

        CliRun run = await harness.RunAsync("install");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            [("COPILOT_OTEL_ENABLED", "true")],
            platform.UserVariableWrites);
        (string gh, IReadOnlyList<string> arguments) =
            Assert.Single(platform.ProcessRuns);
        Assert.Equal(@"C:\gh\gh.exe", gh);
        Assert.Equal(
            [
                "alias",
                "set",
                "ledger",
                "'C:/tools/gh-ledger.exe' \"$@\"",
                "--shell",
                "--clobber",
            ],
            arguments);
        Assert.Equal(
            Normalize($"""
            Set COPILOT_OTEL_ENABLED=true for your user account.
            Set gh alias: gh ledger runs C:\tools\gh-ledger.exe.
            gh-ledger daemon started (pid {Environment.ProcessId}) on http://localhost:4318.
            CopilotLedger is installed. Restart the GitHub Copilot app, VS Code, and open terminals so they pick up COPILOT_OTEL_ENABLED.

            """),
            run.Out);
        using JsonDocument manifest = JsonDocument.Parse(
            await File.ReadAllTextAsync(ManifestPath(temp.Path)));
        Assert.Equal(
            @"C:\tools\gh-ledger.exe",
            manifest.RootElement.GetProperty("executablePath").GetString());
        Assert.True(
            manifest.RootElement.GetProperty("windowsUserVariableSet").GetBoolean());
        Assert.Equal(
            JsonValueKind.Null,
            manifest.RootElement
                .GetProperty("windowsUserVariablePreviousValue").ValueKind);
        Assert.Empty(
            manifest.RootElement.GetProperty("profileFiles").EnumerateArray());
        Assert.True(manifest.RootElement.GetProperty("ghAliasSet").GetBoolean());
    }

    [Fact]
    public async Task IN3_RepeatedWindowsInstallPreservesManifestAndIsIdempotent()
    {
        using TempDirectory temp = new();
        FakeCliPlatform platform = WindowsPlatform();
        await using CliHarness harness = new(temp.Path, platform);
        Assert.Equal(0, (await harness.RunAsync("install")).ExitCode);

        CliRun second = await harness.RunAsync("install");

        Assert.Equal(0, second.ExitCode);
        Assert.Single(platform.UserVariableWrites);
        Assert.StartsWith(
            "COPILOT_OTEL_ENABLED=true is already set for your user account.\n",
            second.Out);
        Assert.Contains(
            $"gh-ledger daemon is already running (pid {Environment.ProcessId}).",
            second.Out);
        using JsonDocument manifest = JsonDocument.Parse(
            await File.ReadAllTextAsync(ManifestPath(temp.Path)));
        Assert.True(
            manifest.RootElement.GetProperty("windowsUserVariableSet").GetBoolean());
        Assert.Equal(
            JsonValueKind.Null,
            manifest.RootElement
                .GetProperty("windowsUserVariablePreviousValue").ValueKind);
    }

    [Fact]
    public async Task IN4_UninstallRestoresPreviousWindowsUserVariable()
    {
        using TempDirectory temp = new();
        FakeCliPlatform platform = WindowsPlatform();
        platform.UserVariables["COPILOT_OTEL_ENABLED"] = "false";
        await using CliHarness harness = new(temp.Path, platform);
        Assert.Equal(0, (await harness.RunAsync("install")).ExitCode);
        Assert.Equal(
            [("COPILOT_OTEL_ENABLED", "true")],
            platform.UserVariableWrites);

        using (JsonDocument manifest = JsonDocument.Parse(
                   await File.ReadAllTextAsync(ManifestPath(temp.Path))))
        {
            Assert.Equal(
                "false",
                manifest.RootElement
                    .GetProperty("windowsUserVariablePreviousValue").GetString());
        }

        CliRun uninstall = await harness.RunAsync("uninstall");

        Assert.Equal(0, uninstall.ExitCode);
        Assert.Equal(
            ("COPILOT_OTEL_ENABLED", "false"),
            platform.UserVariableWrites[^1]);
        Assert.Contains(
            "Restored COPILOT_OTEL_ENABLED=false for your user account.",
            uninstall.Out);
    }

    [Fact]
    public async Task IN5_UnixInstallAddsOneMarkedProfileBlockAndIsIdempotent()
    {
        using TempDirectory temp = new();
        string profile = Path.Combine(temp.Path, "home", ".zshrc");
        FakeCliPlatform platform = UnixPlatform(temp.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(profile)!);
        await File.WriteAllTextAsync(profile, "export FOO=1");
        await using CliHarness harness = new(temp.Path, platform);

        CliRun first = await harness.RunAsync("install");
        string afterFirst = await File.ReadAllTextAsync(profile);
        CliRun second = await harness.RunAsync("install");

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(
            "export FOO=1\n\n# >>> copilot-ledger >>>\nexport COPILOT_OTEL_ENABLED=true\n# <<< copilot-ledger <<<\n",
            afterFirst.Replace("\r\n", "\n"));
        Assert.Equal(afterFirst, await File.ReadAllTextAsync(profile));
        Assert.Empty(platform.UserVariableWrites);
        Assert.Equal(
            "'/opt/bin/gh-ledger' \"$@\"",
            platform.ProcessRuns[0].Arguments[3]);
        Assert.Contains($"Added COPILOT_OTEL_ENABLED=true to {profile}.", first.Out);
        Assert.DoesNotContain("sh.exe", first.Out);
        Assert.Contains(
            $"{profile} already sets COPILOT_OTEL_ENABLED.",
            second.Out);
        using JsonDocument manifest = JsonDocument.Parse(
            await File.ReadAllTextAsync(ManifestPath(temp.Path)));
        Assert.Equal(
            [profile],
            manifest.RootElement.GetProperty("profileFiles")
                .EnumerateArray().Select(item => item.GetString()));
    }

    public static TheoryData<string?, bool, string> UnixProfiles => new()
    {
        { "/bin/bash", true, ".bash_profile" },
        { "/usr/bin/bash", false, ".bashrc" },
        { "/usr/bin/fish", false, ".profile" },
        { null, false, ".profile" },
    };

    [Theory]
    [MemberData(nameof(UnixProfiles))]
    public async Task IN6_UnixShellSelectsExactProfile(
        string? shellPath,
        bool isMacOs,
        string profileName)
    {
        using TempDirectory temp = new();
        FakeCliPlatform platform = UnixPlatform(temp.Path);
        platform.ShellPath = shellPath;
        platform.IsMacOS = isMacOs;
        await using CliHarness harness = new(temp.Path, platform);

        CliRun run = await harness.RunAsync("install");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(
            "# >>> copilot-ledger >>>\nexport COPILOT_OTEL_ENABLED=true\n# <<< copilot-ledger <<<\n",
            (await File.ReadAllTextAsync(
                Path.Combine(temp.Path, "home", profileName))).Replace("\r\n", "\n"));
    }

    public static TheoryData<bool, string> GhFailures => new()
    {
        { true, "GitHub CLI (gh) was not found, so the gh ledger alias was not set. Run gh-ledger directly." },
        { false, "Warning: could not set the gh alias: boom" },
    };

    [Theory]
    [MemberData(nameof(GhFailures))]
    public async Task IN7_MissingOrFailingGhDoesNotFailInstall(
        bool missingGh,
        string expectedMessage)
    {
        using TempDirectory temp = new();
        FakeCliPlatform platform = WindowsPlatform();
        if (missingGh)
        {
            platform.PathExecutables.Remove("gh");
        }
        else
        {
            platform.RunHandler = (_, _, _) =>
                Task.FromResult(new ProcessRunResult(1, "", "boom"));
        }

        await using CliHarness harness = new(temp.Path, platform);
        CliRun run = await harness.RunAsync("install");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains(expectedMessage, run.Out);
        Assert.False(
            JsonDocument.Parse(
                await File.ReadAllTextAsync(ManifestPath(temp.Path)))
                .RootElement.GetProperty("ghAliasSet").GetBoolean());
    }

    [Fact]
    public async Task IN8_InstallRequiresGhLedgerExecutableAndMakesNoChanges()
    {
        using TempDirectory temp = new();
        FakeCliPlatform platform = UnixPlatform(temp.Path);
        platform.ProcessPath = "/usr/share/dotnet/dotnet";
        await using CliHarness harness = new(temp.Path, platform);

        CliRun run = await harness.RunAsync("install");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(
            "Run install from the gh-ledger executable (current process: /usr/share/dotnet/dotnet).\n",
            run.Error);
        Assert.Empty(platform.UserVariableWrites);
        Assert.Empty(platform.ProcessRuns);
        Assert.Empty(platform.DetachedStarts);
        Assert.False(File.Exists(ManifestPath(temp.Path)));
    }

    [Fact]
    public async Task IN9_WindowsDryRunMakesNoChangesAndPrintsExactPlan()
    {
        using TempDirectory temp = new();
        FakeCliPlatform platform = WindowsPlatform();
        await using CliHarness harness = new(temp.Path, platform);

        CliRun run = await harness.RunAsync("install", "--dry-run");

        Assert.Equal(0, run.ExitCode);
        Assert.Empty(platform.UserVariableWrites);
        Assert.Empty(platform.ProcessRuns);
        Assert.Empty(platform.DetachedStarts);
        Assert.False(File.Exists(ManifestPath(temp.Path)));
        Assert.Equal(
            Normalize("""
            Would set COPILOT_OTEL_ENABLED=true for your user account.
            Would set gh alias: gh ledger runs C:\tools\gh-ledger.exe.
            Warning: GitHub CLI needs sh.exe from Git for Windows to run shell aliases, so gh ledger won't work until Git for Windows is installed. Run gh-ledger directly.
            Would start the gh-ledger daemon.
            Dry run: no changes made.

            """),
            run.Out);
    }

    public static TheoryData<string> UninstallCases => new()
    {
        "after-windows",
        "after-unix",
        "no-manifest",
        "no-such-alias",
        "changed-variable",
    };

    [Theory]
    [MemberData(nameof(UninstallCases))]
    public async Task IN10_UninstallReversesOnlyRecordedChanges(string caseName)
    {
        using TempDirectory temp = new();
        FakeCliPlatform platform = caseName == "after-unix"
            ? UnixPlatform(temp.Path)
            : WindowsPlatform();
        await using CliHarness harness = new(temp.Path, platform);

        if (caseName != "no-manifest")
        {
            Assert.Equal(0, (await harness.RunAsync("install")).ExitCode);
        }

        if (caseName == "no-such-alias")
        {
            platform.RunHandler = (_, arguments, _) =>
                Task.FromResult(arguments[1] == "delete"
                    ? new ProcessRunResult(1, "", "no such alias ledger")
                    : new ProcessRunResult(0, "", ""));
        }
        else if (caseName == "changed-variable")
        {
            platform.UserVariables["COPILOT_OTEL_ENABLED"] = "1";
        }

        CliRun uninstall = await harness.RunAsync("uninstall");

        Assert.Equal(0, uninstall.ExitCode);
        if (caseName == "after-windows")
        {
            Assert.Contains("gh-ledger daemon stopped.", uninstall.Out);
            Assert.Equal(
                ("COPILOT_OTEL_ENABLED", (string?)null),
                platform.UserVariableWrites[^1]);
            Assert.Contains(
                "Removed COPILOT_OTEL_ENABLED from your user account.",
                uninstall.Out);
            Assert.Equal(["alias", "delete", "ledger"], platform.ProcessRuns[^1].Arguments);
            Assert.Equal(@"C:\gh\gh.exe", platform.ProcessRuns[^1].FileName);
            Assert.Contains("Removed gh alias: ledger.", uninstall.Out);
            Assert.False(File.Exists(ManifestPath(temp.Path)));
            Assert.EndsWith(
                $"CopilotLedger is uninstalled. Your ledger data is still in {Path.Combine(temp.Path, "data")}.\n",
                uninstall.Out);
        }
        else if (caseName == "after-unix")
        {
            Assert.Equal(
                "export FOO=1\n",
                (await File.ReadAllTextAsync(
                    Path.Combine(temp.Path, "home", ".zshrc"))).Replace("\r\n", "\n"));
        }
        else if (caseName == "no-manifest")
        {
            Assert.Equal(
                "CopilotLedger is not installed; nothing to undo.\n",
                uninstall.Out);
        }
        else if (caseName == "no-such-alias")
        {
            Assert.Contains("Removed gh alias: ledger.", uninstall.Out);
        }
        else
        {
            Assert.Contains(
                "Left COPILOT_OTEL_ENABLED unchanged because it is no longer true.",
                uninstall.Out);
            Assert.Single(platform.UserVariableWrites);
        }
    }

    private static FakeCliPlatform WindowsPlatform() =>
        new()
        {
            IsWindows = true,
            ProcessPath = @"C:\tools\gh-ledger.exe",
            PathExecutables =
            {
                ["gh"] = @"C:\gh\gh.exe",
                ["git"] = @"C:\git\cmd\git.exe",
            },
        };

    private static FakeCliPlatform UnixPlatform(string root)
    {
        string home = Path.Combine(root, "home");
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, ".zshrc"), "export FOO=1");
        return new()
        {
            IsWindows = false,
            HomeDirectory = home,
            ShellPath = "/bin/zsh",
            ProcessPath = "/opt/bin/gh-ledger",
            PathExecutables =
            {
                ["gh"] = "/usr/bin/gh",
            },
        };
    }

    private static string ManifestPath(string root) =>
        Path.Combine(root, "data", "install.json");

    private static string Normalize(string value) => value.Replace("\r\n", "\n");
}
