using CopilotLedger.Core.Breaker;

namespace CopilotLedger.Storage;

public sealed record NewBreakerTrip
{
    public required BreakerRule Rule { get; init; }
    public required string TraceId { get; init; }
    public string? ConversationId { get; init; }
    public required string Repository { get; init; }                     // TraceActivity.Repository
    public required string Branch { get; init; }                         // TraceActivity.Branch
    public string? RepositoryRoot { get; init; }                         // TraceActivity.RepositoryRoot
    public required DateTimeOffset TrippedAt { get; init; }
    public required string Detail { get; init; }                         // "180.00 AI credits in 5 minutes" or "5 failed tool calls in a row"
    public decimal? WindowCostUsd { get; init; }                         // CreditVelocity only
    public int? WindowMinutes { get; init; }                             // CreditVelocity only
    public int? FailureCount { get; init; }                              // RetryChurn only
}

public sealed record BreakerTrip
{
    public required long Id { get; init; }
    public required BreakerRule Rule { get; init; }
    public required string TraceId { get; init; }
    public string? ConversationId { get; init; }
    public required string Repository { get; init; }
    public required string Branch { get; init; }
    public string? RepositoryRoot { get; init; }
    public required DateTimeOffset TrippedAt { get; init; }
    public required string Detail { get; init; }
    public decimal? WindowCostUsd { get; init; }
    public int? WindowMinutes { get; init; }
    public int? FailureCount { get; init; }
    public required DateTimeOffset RecordedAt { get; init; }
    public DateTimeOffset? NotifiedAt { get; init; }
    public DateTimeOffset? ResetAt { get; init; }
}
