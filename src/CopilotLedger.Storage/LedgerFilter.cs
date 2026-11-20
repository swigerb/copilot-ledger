namespace CopilotLedger.Storage;

public sealed record LedgerFilter
{
    public static LedgerFilter All { get; } = new();
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public string? Repository { get; init; }
    public string? Branch { get; init; }
}
