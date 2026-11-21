namespace CopilotLedger.Cli.Install;

internal sealed record InstallManifest
{
    public required int SchemaVersion { get; init; }                         // 1
    public required DateTimeOffset InstalledAt { get; init; }                // manifest write time
    public required string ExecutablePath { get; init; }                     // running binary
    public required bool WindowsUserVariableSet { get; init; }               // managed by this manifest
    public required string? WindowsUserVariablePreviousValue { get; init; }  // restored by uninstall
    public required IReadOnlyList<string> ProfileFiles { get; init; }        // shell profiles with the block
    public required bool GhAliasSet { get; init; }                           // alias was changed by install
}
