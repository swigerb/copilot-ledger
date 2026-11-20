using CopilotLedger.Core.Telemetry;

namespace CopilotLedger.Otlp.Normalization;

public sealed record NormalizedTraceBatch
{
    public required IReadOnlyList<NormalizedGenAiSpan> InferenceSpans { get; init; }
    public required IReadOnlyList<GenAiSpanLink> Links { get; init; }
    public required int ReceivedSpanCount { get; init; }
    public required int RejectedSpanCount { get; init; }
    public string? FirstRejectionReason { get; init; }
}
