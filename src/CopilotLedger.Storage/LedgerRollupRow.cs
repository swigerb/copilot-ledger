using CopilotLedger.Core.Pricing;

namespace CopilotLedger.Storage;

public sealed record LedgerRollupRow
{
    public string? Repository { get; init; }
    public string? Branch { get; init; }
    public string? Model { get; init; }
    public string? Agent { get; init; }
    public BillingChannel? Channel { get; init; }
    public required long SpanCount { get; init; }
    public required long UnpricedSpanCount { get; init; }
    public required long InputTokens { get; init; }
    public required long OutputTokens { get; init; }
    public required long CacheReadInputTokens { get; init; }
    public required long CacheWriteInputTokens { get; init; }
    public required decimal AiCredits { get; init; }
    public required decimal AiCreditsUsd { get; init; }
    public required decimal FoundryByokUsd { get; init; }
    public required decimal LocalSavedUsd { get; init; }
}
