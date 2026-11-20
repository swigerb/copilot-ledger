using CopilotLedger.Otlp.Attribution;
using CopilotLedger.Tests.TestSupport;

namespace CopilotLedger.Tests.Otlp;

public sealed class CopilotSessionWorkspaceLocatorTests
{
    private const string SessionId = "d9b2d63d-a233-4123-847a-5a1f3c8e6e1f";

    public static TheoryData<Func<string, string>> ValidCwdLines => new()
    {
        directory => $"cwd: {directory}",
        directory => $"cwd: {directory} # note",
        directory => $"cwd: \"{directory.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
        directory => $"cwd: '{directory}'",
    };

    [Theory]
    [MemberData(nameof(ValidCwdLines))]
    public void WL1_ValidYamlCwdForms_ReturnDirectory(Func<string, string> cwdLine)
    {
        using TempDirectory temp = new();
        (CopilotSessionWorkspaceLocator locator, string directory) =
            CreateLocator(temp, cwdLine);

        Assert.Equal(directory, locator.GetWorkingDirectory(SessionId));
    }

    public static TheoryData<string?> InvalidFiles => new()
    {
        null,
        "id: " + SessionId + "\nbranch: x\n",
        "cwd: relative/path\n",
        "cwd:\n",
        "  cwd: /not-at-column-zero\n",
    };

    [Theory]
    [MemberData(nameof(InvalidFiles))]
    public void WL2_MissingOrInvalidCwd_ReturnsNull(string? contents)
    {
        using TempDirectory temp = new();
        string home = Path.Combine(temp.Path, "copilot-home");
        if (contents is not null)
        {
            Write(home, contents);
        }

        Assert.Null(new CopilotSessionWorkspaceLocator(home)
            .GetWorkingDirectory(SessionId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../etc")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData(".hidden")]
    public void WL3_InvalidSessionIds_ReturnNull(string id)
    {
        using TempDirectory temp = new();
        Assert.Null(new CopilotSessionWorkspaceLocator(temp.Path)
            .GetWorkingDirectory(id));
    }

    [Fact]
    public void WL3_TooLongSessionId_ReturnsNull()
    {
        using TempDirectory temp = new();
        Assert.Null(new CopilotSessionWorkspaceLocator(temp.Path)
            .GetWorkingDirectory(new string('a', 129)));
    }

    [Fact]
    public void WL4_CachesMissingAndPresentFilesUntilExpiry()
    {
        using TempDirectory temp = new();
        ManualTimeProvider time = new(
            new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero));
        string directory = Path.Combine(temp.Path, "work");
        string home = Path.Combine(temp.Path, "copilot-home");
        Write(home, $"id: {SessionId}\ncwd: {directory}\nbranch: x\n");
        CopilotSessionWorkspaceLocator locator = new(home, time);

        Assert.Equal(directory, locator.GetWorkingDirectory(SessionId));
        File.Delete(WorkspaceFile(home));
        Assert.Equal(directory, locator.GetWorkingDirectory(SessionId));
        time.Advance(TimeSpan.FromSeconds(61));
        Assert.Null(locator.GetWorkingDirectory(SessionId));
    }

    private static (CopilotSessionWorkspaceLocator Locator, string Directory) CreateLocator(
        TempDirectory temp,
        Func<string, string> cwdLine)
    {
        string home = Path.Combine(temp.Path, "copilot-home");
        string directory = Path.Combine(temp.Path, "work");
        Write(home, $"id: {SessionId}\n{cwdLine(directory)}\nbranch: x\n");
        return (new CopilotSessionWorkspaceLocator(home), directory);
    }

    private static void Write(string home, string contents)
    {
        string file = WorkspaceFile(home);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, contents);
    }

    private static string WorkspaceFile(string home) =>
        Path.Combine(home, "session-state", SessionId, "workspace.yaml");
}
