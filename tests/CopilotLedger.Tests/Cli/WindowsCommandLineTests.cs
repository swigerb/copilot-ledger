using CopilotLedger.Cli;

namespace CopilotLedger.Tests.Cli;

public sealed class WindowsCommandLineTests
{
    private const string Executable = @"C:\Program Files\gh-ledger\gh-ledger.exe";

    public static TheoryData<string, string> Arguments => new()
    {
        { "daemon", "daemon" },
        { "4318", "4318" },
        { @"C:\Users\O'Brien Smith\data\daemon.log", "\"C:\\Users\\O'Brien Smith\\data\\daemon.log\"" },
        { "", "\"\"" },
        { "a\"b", "\"a\\\"b\"" },
        { @"C:\a b\", "\"C:\\a b\\\\\"" },
        { "a\\\\\"b", "\"a\\\\\\\\\\\"b\"" },
        { "tab\there", "\"tab\there\"" },
        { @"back\slash", @"back\slash" },
        { @"end\", @"end\" },
        { "\"quoted\"", "\"\\\"quoted\\\"\"" },
        { "\u00FCn\u00EFc\u00F6d\u00E9 \u2713", "\"\u00FCn\u00EFc\u00F6d\u00E9 \u2713\"" },
        { @"\\server\share\x y\", "\"\\\\server\\share\\x y\\\\\"" },
    };

    [Theory]
    [MemberData(nameof(Arguments))]
    public void WC1_EachArgumentIsQuotedForTheCRuntime(string argument, string written)
    {
        Assert.Equal($"\"{Executable}\" {written}", WindowsCommandLine.Build(Executable, [argument]));
    }

    [Fact]
    public void WC2_DaemonCommandLineIsExact()
    {
        Assert.Equal(
            "\"C:\\Program Files\\gh-ledger\\gh-ledger.exe\" daemon run --port 4318 --log-file \"C:\\Users\\O'Brien Smith\\data\\daemon.log\"",
            WindowsCommandLine.Build(
                Executable,
                ["daemon", "run", "--port", "4318", "--log-file", @"C:\Users\O'Brien Smith\data\daemon.log"]));
        Assert.Equal("\"C:\\gh-ledger.exe\"", WindowsCommandLine.Build(@"C:\gh-ledger.exe", []));
    }
}
