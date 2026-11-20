using CopilotLedger.Otlp.Decoding;
using CopilotLedger.Tests.TestSupport;

namespace CopilotLedger.Tests.Otlp;

public sealed class OtlpProtobufDecodingTests
{
    [Fact]
    public void PB1_GoldenPayload_DecodesEveryGoldenField()
    {
        OtlpFixtures.AssertPGold1(OtlpTraceDecoder.DecodeProtobuf(OtlpFixtures.PGold1));
    }

    [Fact]
    public void PB2_GoldenRecipe_ProducesExactGoldenBytes()
    {
        Assert.Equal(OtlpFixtures.PGold1, OtlpFixtures.PGold1Writer().ToArray());
    }

    [Fact]
    public void PB3_EmptyPayload_ReturnsNoResourceSpans()
    {
        OtlpTraceRequest request = OtlpTraceDecoder.DecodeProtobuf(Array.Empty<byte>());

        Assert.Empty(request.ResourceSpans);
    }

    public static TheoryData<byte[]> PB4MalformedPayloads => new()
    {
        OtlpFixtures.PGold1[..^1],
        Convert.FromHexString("0b"),
        Convert.FromHexString("0affffffff0f"),
        Convert.FromHexString("08ffffffffffffffffff7f"),
        Convert.FromHexString("00"),
        Convert.FromHexString("0a020a"),
        Convert.FromHexString("0a030a0105"),
    };

    [Theory]
    [MemberData(nameof(PB4MalformedPayloads))]
    public void PB4_MalformedPayload_ThrowsOtlpDecodeException(byte[] payload)
    {
        Assert.Throws<OtlpDecodeException>(() => OtlpTraceDecoder.DecodeProtobuf(payload));
    }

    [Fact]
    public void PB5_InvalidIds_BecomeNull()
    {
        OtlpProtoWriter span = new OtlpProtoWriter()
            .Bytes(1, Enumerable.Repeat((byte)1, 15).ToArray())
            .Bytes(2, new byte[8])
            .Bytes(4, new byte[8])
            .Fixed64(7, 1);
        OtlpTraceRequest request = DecodeSingleSpan(span);
        OtlpSpan decoded = Assert.Single(Assert.Single(request.ResourceSpans).Spans);

        Assert.Null(decoded.TraceId);
        Assert.Null(decoded.SpanId);
        Assert.Null(decoded.ParentSpanId);
    }

    [Fact]
    public void PB6_MalformedAttributeValues_PreserveRecognizedValuesAndDropEmptyKey()
    {
        OtlpProtoWriter invalidString = new OtlpProtoWriter()
            .Bytes(1, [0x67, 0xc3, 0x28])
            .Message(2, new OtlpProtoWriter().String(1, "x"));
        OtlpProtoWriter negativeInt = new OtlpProtoWriter()
            .String(1, "negative")
            .Message(2, new OtlpProtoWriter().Varint(3, ulong.MaxValue));
        OtlpProtoWriter boolean = new OtlpProtoWriter()
            .String(1, "boolean")
            .Message(2, new OtlpProtoWriter().Varint(2, 1));
        OtlpProtoWriter missingValue = new OtlpProtoWriter().String(1, "missing");
        OtlpProtoWriter emptyKey = new OtlpProtoWriter()
            .String(1, "")
            .Message(2, new OtlpProtoWriter().String(1, "drop"));
        OtlpProtoWriter span = new OtlpProtoWriter()
            .Message(9, invalidString)
            .Message(9, negativeInt)
            .Message(9, boolean)
            .Message(9, missingValue)
            .Message(9, emptyKey);

        OtlpSpan decoded = Assert.Single(Assert.Single(DecodeSingleSpan(span).ResourceSpans).Spans);

        Assert.Collection(
            decoded.Attributes,
            attribute => OtlpFixtures.AssertAttribute(attribute, "g\uFFFD(", OtlpValueKind.String, "x"),
            attribute => OtlpFixtures.AssertAttribute(attribute, "negative", OtlpValueKind.Int, -1),
            attribute => OtlpFixtures.AssertAttribute(attribute, "boolean", OtlpValueKind.Bool, true),
            attribute => OtlpFixtures.AssertAttribute(attribute, "missing", OtlpValueKind.Empty));
    }

    [Fact]
    public void PB7_WrongWireTypeAndUnknownField_AreSkipped()
    {
        OtlpProtoWriter span = new OtlpProtoWriter()
            .Varint(1, 1)
            .Bytes(2, Convert.FromHexString("eee19b7ec3c1b174"));
        OtlpProtoWriter scopeSpans = new OtlpProtoWriter().Message(2, span);
        OtlpProtoWriter resourceSpans = new OtlpProtoWriter().Message(2, scopeSpans);
        byte[] payload = new OtlpProtoWriter()
            .Message(1, resourceSpans)
            .Raw(0x12, 0x00)
            .ToArray();

        OtlpSpan decoded = Assert.Single(Assert.Single(
            OtlpTraceDecoder.DecodeProtobuf(payload).ResourceSpans).Spans);

        Assert.Null(decoded.TraceId);
        Assert.Equal("eee19b7ec3c1b174", decoded.SpanId);
    }

    private static OtlpTraceRequest DecodeSingleSpan(OtlpProtoWriter span)
    {
        OtlpProtoWriter scopeSpans = new OtlpProtoWriter().Message(2, span);
        OtlpProtoWriter resourceSpans = new OtlpProtoWriter().Message(2, scopeSpans);
        return OtlpTraceDecoder.DecodeProtobuf(new OtlpProtoWriter().Message(1, resourceSpans).ToArray());
    }
}
