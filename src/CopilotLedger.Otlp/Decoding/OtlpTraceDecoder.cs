namespace CopilotLedger.Otlp.Decoding;

public static class OtlpTraceDecoder
{
    // Binary protobuf ExportTraceServiceRequest. Throws OtlpDecodeException for malformed input.
    public static OtlpTraceRequest DecodeProtobuf(ReadOnlyMemory<byte> payload) =>
        OtlpProtobufTraceReader.Decode(payload.Span);

    // OTLP/JSON ExportTraceServiceRequest in UTF-8. Throws OtlpDecodeException for malformed input.
    public static OtlpTraceRequest DecodeJson(ReadOnlyMemory<byte> utf8Json) =>
        OtlpJsonTraceReader.Decode(utf8Json);
}
