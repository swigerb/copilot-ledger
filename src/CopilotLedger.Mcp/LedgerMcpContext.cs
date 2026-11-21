using CopilotLedger.Core.Git;

namespace CopilotLedger.Mcp;

public sealed record LedgerMcpContext
{
    public required string DatabasePath { get; init; }
    public required string WorkingDirectory { get; init; }                 // the server process's working directory
    public required Func<string, string?> GetEnvironmentVariable { get; init; }
    public required IGitContextResolver GitResolver { get; init; }
    public required TimeProvider TimeProvider { get; init; }               // the store's clock
    public required string Version { get; init; }                          // serverInfo.version
}
