namespace CopilotLedger.Core;

public static class LedgerPaths
{
    public static string DataDirectory { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".copilot-ledger");

    public static string DefaultDatabasePath { get; } =
        Path.Combine(DataDirectory, "ledger.db");

    public static string DefaultRatesOverlayPath { get; } =
        Path.Combine(DataDirectory, "rates.json");
}
