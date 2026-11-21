namespace CopilotLedger.Core.Breaker;

public sealed record TraceActivity
{
    public required string TraceId { get; init; }
    public string? ConversationId { get; init; }                         // MAX over the trace's spans rows; when that's null, MAX over its span_links rows
    public required string Repository { get; init; }                     // MAX(COALESCE(repo_slug, repo_root, '(none)')); "(none)" with no spans rows
    public required string Branch { get; init; }                         // MAX(COALESCE(branch_name, '(none)')); "(none)" with no spans rows
    public string? RepositoryRoot { get; init; }                         // MAX(repo_root)
    public required long SpanCount { get; init; }                        // the trace's spans rows
    public required IReadOnlyList<CostEvent> Costs { get; init; }        // spans rows with cost_usd_pico > 0, by end time, then span ID
    public required IReadOnlyList<ToolCallEvent> ToolCalls { get; init; } // execute_tool links, by start time, then span ID
    public required IReadOnlyList<CommitObservation> Commits { get; init; } // spans rows with a head_commit_sha, by start time, then span ID
}
