namespace CopilotLedger.Cli.Install;

// `gh extension install swigerb/gh-ledger` saves the release asset for the platform as
// <gh data directory>/extensions/gh-ledger/gh-ledger (gh-ledger.exe on Windows) next to a manifest.yml, and runs it for
// `gh ledger ...`. Paths are split on both '/' and '\' so tests can use either style on any OS.
internal static class GhExtension
{
    public static bool IsInstalled(ICliPlatform platform)
    {
        string? executable = platform.ProcessPath;
        if (executable is null)
        {
            return false;
        }

        int separator = LastSeparator(executable);
        if (separator < 0)
        {
            return false;
        }

        string name = executable[(separator + 1)..];
        string directory = executable[..separator];
        int parentSeparator = LastSeparator(directory);
        if (parentSeparator < 0)
        {
            return false;
        }

        string parent = directory[..parentSeparator];
        return (name.Equals("gh-ledger", StringComparison.OrdinalIgnoreCase)
                || name.Equals("gh-ledger.exe", StringComparison.OrdinalIgnoreCase))
            && directory[(parentSeparator + 1)..].Equals("gh-ledger", StringComparison.OrdinalIgnoreCase)
            && parent[(LastSeparator(parent) + 1)..].Equals("extensions", StringComparison.OrdinalIgnoreCase)
            && platform.FileExists(directory + executable[separator] + "manifest.yml");
    }

    // How to run gh-ledger in messages: "gh ledger" for the extension, else "gh-ledger".
    public static string CommandName(ICliPlatform platform) =>
        IsInstalled(platform) ? "gh ledger" : "gh-ledger";

    private static int LastSeparator(string path) =>
        Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
}
