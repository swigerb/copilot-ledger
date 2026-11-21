namespace CopilotLedger.Core.Breaker;

public sealed record CostEvent(
    string SpanId,                                     // spans.span_id
    DateTimeOffset EndTime,                            // spans.end_time_unix_ms
    decimal CostUsd);                                  // PicoUsd.ToUsd(spans.cost_usd_pico): GitHub AI credits or BYOK

public sealed record ToolCallEvent(
    string SpanId,                                     // span_links.span_id of an execute_tool span
    DateTimeOffset StartTime,                          // span_links.start_time_unix_ms
    string? ToolName,                                  // span_links.tool_name
    long? ExitCode,                                    // span_links.tool_exit_code
    int StatusCode)                                    // span_links.status_code
{
    public bool IsFailure => (ExitCode is long code && code != 0) || StatusCode == 2;
    public bool IsSuccessfulCommand => ExitCode == 0 && StatusCode != 2;
}

public sealed record CommitObservation(
    string SpanId,                                     // spans.span_id
    DateTimeOffset StartTime,                          // spans.start_time_unix_ms
    string CommitSha);                                 // spans.head_commit_sha
