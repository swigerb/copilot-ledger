using CopilotLedger.Core.Telemetry;

namespace CopilotLedger.Otlp.Normalization;

public sealed record NormalizedTraceBatch
{
    public required IReadOnlyList<NormalizedGenAiSpan> InferenceSpans { get; init; }  // accepted inference spans, in request order
    public required IReadOnlyList<GenAiSpanLink> Links { get; init; }                  // accepted indexed spans, in request order
    public required int ReceivedSpanCount { get; init; }
    public required int RejectedSpanCount { get; init; }
    public string? FirstRejectionReason { get; init; }                                 // null when RejectedSpanCount is 0
}
