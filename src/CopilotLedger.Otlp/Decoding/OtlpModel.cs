namespace CopilotLedger.Otlp.Decoding;

public enum OtlpValueKind
{
    Empty,          // no value field
    String,
    Bool,
    Int,
    Double,         // recognized; the value isn't kept
    Bytes,          // recognized; the value isn't kept
    Array,          // recognized; the value isn't kept
    KeyValueList,   // recognized; the value isn't kept
}

public sealed record OtlpAnyValue
{
    public static OtlpAnyValue Empty { get; } = new() { Kind = OtlpValueKind.Empty };
    public required OtlpValueKind Kind { get; init; }
    public string? StringValue { get; init; }
    public bool BoolValue { get; init; }
    public long IntValue { get; init; }
}

public sealed record OtlpAttribute(string Key, OtlpAnyValue Value);

public sealed record OtlpSpan
{
    public string? TraceId { get; init; }
    public string? SpanId { get; init; }
    public string? ParentSpanId { get; init; }
    public string Name { get; init; } = "";
    public ulong StartTimeUnixNano { get; init; }
    public ulong EndTimeUnixNano { get; init; }
    public IReadOnlyList<OtlpAttribute> Attributes { get; init; } = [];
}

public sealed record OtlpResourceSpans(
    IReadOnlyList<OtlpAttribute> ResourceAttributes,
    IReadOnlyList<OtlpSpan> Spans);

public sealed record OtlpTraceRequest(IReadOnlyList<OtlpResourceSpans> ResourceSpans)
{
    public int SpanCount => ResourceSpans.Sum(r => r.Spans.Count);
}
