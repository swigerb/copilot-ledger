using CopilotLedger.Otlp.Decoding;

namespace CopilotLedger.Tests.TestSupport;

public static class OtlpRequests
{
    public static OtlpAttribute Str(string key, string value) =>
        new(key, new OtlpAnyValue { Kind = OtlpValueKind.String, StringValue = value });

    public static OtlpAttribute Int(string key, long value) =>
        new(key, new OtlpAnyValue { Kind = OtlpValueKind.Int, IntValue = value });

    public static OtlpAttribute Dbl(string key) =>
        new(key, new OtlpAnyValue { Kind = OtlpValueKind.Double });

    public static OtlpAttribute Emp(string key) =>
        new(key, OtlpAnyValue.Empty);

    public static OtlpSpan Span(
        string? traceId = "5b8efff798038103d269b633813fc60c",
        string? spanId = "eee19b7ec3c1b174",
        string? parentSpanId = null,
        string name = "",
        ulong startNs = 1795199604707000000,
        ulong endNs = 1795199606429000000,
        params OtlpAttribute[] attributes) =>
        new()
        {
            TraceId = traceId,
            SpanId = spanId,
            ParentSpanId = parentSpanId,
            Name = name,
            StartTimeUnixNano = startNs,
            EndTimeUnixNano = endNs,
            Attributes = attributes,
        };

    public static OtlpTraceRequest Request(
        IReadOnlyList<OtlpAttribute> resourceAttributes,
        params OtlpSpan[] spans) =>
        new([new OtlpResourceSpans(resourceAttributes, spans)]);

    public static IReadOnlyList<OtlpAttribute> CopilotResource =>
        [Str("service.name", "github-copilot")];
}
