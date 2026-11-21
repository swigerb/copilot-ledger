using CopilotLedger.Otlp.Decoding;
using CopilotLedger.Tests.TestSupport;

namespace CopilotLedger.Tests.Otlp;

public sealed class OtlpJsonDecodingTests
{
    [Fact]
    public void JS1_GoldenPayload_DecodesEveryGoldenField()
    {
        OtlpFixtures.AssertPGold1(OtlpTraceDecoder.DecodeJson(OtlpFixtures.Utf8(OtlpFixtures.PGold1Json)));
    }

    [Fact]
    public void JS2_CopilotPayload_DecodesBothSpansAndUnknownFields()
    {
        OtlpTraceRequest request = OtlpTraceDecoder.DecodeJson(
            OtlpFixtures.Utf8(OtlpFixtures.CopilotChatTraceJson));
        OtlpResourceSpans resourceSpans = Assert.Single(request.ResourceSpans);

        Assert.Equal(2, resourceSpans.ResourceAttributes.Count);
        Assert.Equal(2, request.SpanCount);
        OtlpSpan first = resourceSpans.Spans[0];
        Assert.Equal("eee19b7ec3c1b173", first.ParentSpanId);
        Assert.Equal(10, first.Attributes.Count);
        OtlpFixtures.AssertAttribute(
            Assert.Single(first.Attributes, attribute => attribute.Key == "github.copilot.nano_aiu"),
            "github.copilot.nano_aiu",
            OtlpValueKind.Double);
        OtlpFixtures.AssertAttribute(
            Assert.Single(first.Attributes, attribute => attribute.Key == "gen_ai.response.finish_reasons"),
            "gen_ai.response.finish_reasons",
            OtlpValueKind.Array);
        OtlpFixtures.AssertAttribute(
            Assert.Single(first.Attributes, attribute => attribute.Key == "gen_ai.usage.input_tokens"),
            "gen_ai.usage.input_tokens",
            OtlpValueKind.Int,
            17194);
        OtlpSpan second = resourceSpans.Spans[1];
        Assert.Null(second.ParentSpanId);
        Assert.Equal(1795199604679000000UL, second.StartTimeUnixNano);
        Assert.Equal(1795199606444000000UL, second.EndTimeUnixNano);
    }

    public static TheoryData<byte[]> JS3EmptyRequests => new()
    {
        OtlpFixtures.Utf8("{}"),
        OtlpFixtures.Utf8("""{"resourceSpans":[]}"""),
        OtlpFixtures.Utf8("""{"resourceSpans":null}"""),
        Convert.FromHexString("EFBBBF7B7D"),
    };

    [Theory]
    [MemberData(nameof(JS3EmptyRequests))]
    public void JS3_EmptyRequest_ReturnsNoResourceSpans(byte[] payload)
    {
        Assert.Empty(OtlpTraceDecoder.DecodeJson(payload).ResourceSpans);
    }

    public static TheoryData<string> JS4MalformedRequests => new()
    {
        "{",
        "[]",
        """{"resourceSpans":{}}""",
        """{"resourceSpans":[1]}""",
        """{"resourceSpans":[{"scopeSpans":[{"spans":{}}]}]}""",
        """{"resourceSpans":[{"resource":"x"}]}""",
        """{"resourceSpans":[],}""",
        """{"resourceSpans":[]} // c""",
    };

    [Theory]
    [MemberData(nameof(JS4MalformedRequests))]
    public void JS4_MalformedRequest_ThrowsOtlpDecodeException(string json)
    {
        Assert.Throws<OtlpDecodeException>(() => OtlpTraceDecoder.DecodeJson(OtlpFixtures.Utf8(json)));
    }

    [Fact]
    public void JS5_SnakeCaseFields_AreIgnored()
    {
        const string json = """{"resourceSpans":[{"scopeSpans":[{"spans":[{"trace_id":"5b8efff798038103d269b633813fc60c","span_id":"eee19b7ec3c1b174","startTimeUnixNano":"1"}]}]}]}""";

        OtlpSpan span = Assert.Single(Assert.Single(
            OtlpTraceDecoder.DecodeJson(OtlpFixtures.Utf8(json)).ResourceSpans).Spans);

        Assert.Null(span.TraceId);
        Assert.Null(span.SpanId);
        Assert.Equal(1UL, span.StartTimeUnixNano);
    }

    public static TheoryData<string, OtlpValueKind, object?> JS6Values => new()
    {
        { """{"intValue":"12x"}""", OtlpValueKind.Empty, null },
        { """{"intValue":1.5}""", OtlpValueKind.Empty, null },
        { """{"intValue":true}""", OtlpValueKind.Empty, null },
        { """{"intValue":"-7"}""", OtlpValueKind.Int, -7L },
        { """{"intValue":9223372036854775807}""", OtlpValueKind.Int, long.MaxValue },
        { """{"doubleValue":"NaN"}""", OtlpValueKind.Double, null },
        { "{}", OtlpValueKind.Empty, null },
        { """{"stringValue":"a","intValue":"1"}""", OtlpValueKind.String, "a" },
        { """{"foo":1,"boolValue":false}""", OtlpValueKind.Bool, false },
        { """{"stringValue":5}""", OtlpValueKind.Empty, null },
    };

    [Theory]
    [MemberData(nameof(JS6Values))]
    public void JS6_AnyValue_UsesOnlyTheFirstRecognizedProperty(
        string valueJson,
        OtlpValueKind expectedKind,
        object? expectedValue)
    {
        string json = $$"""{"resourceSpans":[{"scopeSpans":[{"spans":[{"attributes":[{"key":"k","value":{{valueJson}}}]}]}]}]}""";

        OtlpAttribute attribute = Assert.Single(Assert.Single(Assert.Single(
            OtlpTraceDecoder.DecodeJson(OtlpFixtures.Utf8(json)).ResourceSpans).Spans).Attributes);

        OtlpFixtures.AssertAttribute(attribute, "k", expectedKind, expectedValue);
    }

    [Fact]
    public void JS7_InvalidAndLargeTimes_AreReadWithoutRounding()
    {
        const string invalidStart = """{"resourceSpans":[{"scopeSpans":[{"spans":[{"startTimeUnixNano":"abc"}]}]}]}""";
        const string largeStart = """{"resourceSpans":[{"scopeSpans":[{"spans":[{"startTimeUnixNano":1795199604707000000}]}]}]}""";
        const string negativeEnd = """{"resourceSpans":[{"scopeSpans":[{"spans":[{"endTimeUnixNano":"-1"}]}]}]}""";

        OtlpSpan invalid = SingleSpan(invalidStart);
        OtlpSpan large = SingleSpan(largeStart);
        OtlpSpan negative = SingleSpan(negativeEnd);

        Assert.Equal(0UL, invalid.StartTimeUnixNano);
        Assert.Equal(1795199604707000000UL, large.StartTimeUnixNano);
        Assert.Equal(0UL, negative.EndTimeUnixNano);
    }

    [Fact]
    public void JS8_UnpairedSurrogates_ProduceNullOrEmptyFieldsAndSkipUnreadableKeys()
    {
        const string json = """
            {"resourceSpans":[{"scopeSpans":[{"spans":[{"traceId":"5b8efff798038103d269b633813fc60c","spanId":"eee19b7ec3c1b174","parentSpanId":"\udc00","\udc00":1,"name":"a\ud800","startTimeUnixNano":"1","attributes":[{"key":"k\ud800","value":{"stringValue":"x"}},{"key":"gen_ai.agent.name","value":{"stringValue":"a\ud800b"}},{"key":"gen_ai.operation.name","value":{"stringValue":"chat"}}]}]}]}]}
            """;

        OtlpSpan span = SingleSpan(json);

        Assert.Equal("5b8efff798038103d269b633813fc60c", span.TraceId);
        Assert.Equal("eee19b7ec3c1b174", span.SpanId);
        Assert.Null(span.ParentSpanId);
        Assert.Equal("", span.Name);
        Assert.Equal(1UL, span.StartTimeUnixNano);
        Assert.Collection(
            span.Attributes,
            attribute => OtlpFixtures.AssertAttribute(
                attribute,
                "gen_ai.agent.name",
                OtlpValueKind.Empty),
            attribute => OtlpFixtures.AssertAttribute(
                attribute,
                "gen_ai.operation.name",
                OtlpValueKind.String,
                "chat"));
    }

    // JS9: an unreadable property name (an escaped lone surrogate, long enough that System.Text.Json throws when it
    // compares the name) in every object, placed first and placed last.
    public static TheoryData<string> UnreadableNameRequests => new()
    {
        """{"\ud800abcdefghijklmnop":1,"resourceSpans":[{"\ud800abcdefghijklmnop":1,"resource":{"\ud800abcdefghijklmnop":1,"attributes":[{"\ud800abcdefghijklmnop":1,"key":"service.name","value":{"\ud800abcdefghijklmnop":1,"stringValue":"github-copilot"}}]},"scopeSpans":[{"\ud800abcdefghijklmnop":1,"spans":[{"\ud800abcdefghijklmnop":1,"traceId":"5b8efff798038103d269b633813fc60c","spanId":"eee19b7ec3c1b174","name":"chat mai-code-2","startTimeUnixNano":"1795199712404000000","endTimeUnixNano":"1795199714391000000","attributes":[{"\ud800abcdefghijklmnop":1,"key":"gen_ai.operation.name","value":{"\ud800abcdefghijklmnop":1,"stringValue":"chat"}}]}]}]}]}""",
        """{"resourceSpans":[{"resource":{"attributes":[{"key":"service.name","value":{"stringValue":"github-copilot","\ud800abcdefghijklmnop":1},"\ud800abcdefghijklmnop":1}],"\ud800abcdefghijklmnop":1},"scopeSpans":[{"spans":[{"traceId":"5b8efff798038103d269b633813fc60c","spanId":"eee19b7ec3c1b174","name":"chat mai-code-2","startTimeUnixNano":"1795199712404000000","endTimeUnixNano":"1795199714391000000","attributes":[{"key":"gen_ai.operation.name","value":{"stringValue":"chat","\ud800abcdefghijklmnop":1},"\ud800abcdefghijklmnop":1}],"\ud800abcdefghijklmnop":1}],"\ud800abcdefghijklmnop":1}],"\ud800abcdefghijklmnop":1}],"\ud800abcdefghijklmnop":1}""",
    };

    [Theory]
    [MemberData(nameof(UnreadableNameRequests))]
    public void JS9_UnreadablePropertyNames_AreIgnored(string json)
    {
        OtlpTraceRequest request = OtlpTraceDecoder.DecodeJson(OtlpFixtures.Utf8(json));
        OtlpResourceSpans resourceSpans = Assert.Single(request.ResourceSpans);

        OtlpFixtures.AssertAttribute(
            Assert.Single(resourceSpans.ResourceAttributes),
            "service.name",
            OtlpValueKind.String,
            "github-copilot");
        OtlpSpan span = Assert.Single(resourceSpans.Spans);
        Assert.Equal("5b8efff798038103d269b633813fc60c", span.TraceId);
        Assert.Equal("eee19b7ec3c1b174", span.SpanId);
        Assert.Null(span.ParentSpanId);
        Assert.Equal("chat mai-code-2", span.Name);
        Assert.Equal(1795199712404000000UL, span.StartTimeUnixNano);
        Assert.Equal(1795199714391000000UL, span.EndTimeUnixNano);
        OtlpFixtures.AssertAttribute(
            Assert.Single(span.Attributes),
            "gen_ai.operation.name",
            OtlpValueKind.String,
            "chat");
    }

    public static TheoryData<string, string, int> JS10Statuses => new()
    {
        { "error", ",\"status\":{\"code\":2,\"message\":\"Tool failed\"}", 2 },
        { "unset", ",\"status\":{\"message\":\"\",\"code\":0}", 0 },
        { "ok", ",\"status\":{\"code\":1}", 1 },
        { "string-number", ",\"status\":{\"code\":\"2\"}", 0 },
        { "string-name", ",\"status\":{\"code\":\"STATUS_CODE_ERROR\"}", 0 },
        { "unknown-code", ",\"status\":{\"code\":3}", 0 },
        { "decimal-code", ",\"status\":{\"code\":2.5}", 0 },
        { "last-wins", ",\"status\":{\"code\":1,\"code\":2}", 2 },
        { "null", ",\"status\":null", 0 },
        { "string-status", ",\"status\":\"error\"", 0 },
        { "missing", "", 0 },
    };

    [Theory]
    [MemberData(nameof(JS10Statuses))]
    public void JS10_Status_IsDecodedInEverySupportedShape(
        string caseName,
        string status,
        int expectedStatusCode)
    {
        _ = caseName;
        string json = $$"""{"resourceSpans":[{"scopeSpans":[{"spans":[{"traceId":"5b8efff798038103d269b633813fc60c","spanId":"eee19b7ec3c1b174","name":"execute_tool powershell pwsh.exe","startTimeUnixNano":"1795220217552000000"{{status}}}]}]}]}""";

        OtlpSpan span = SingleSpan(json);

        Assert.Equal("eee19b7ec3c1b174", span.SpanId);
        Assert.Equal(expectedStatusCode, span.StatusCode);
    }

    private static OtlpSpan SingleSpan(string json) =>
        Assert.Single(Assert.Single(
            OtlpTraceDecoder.DecodeJson(OtlpFixtures.Utf8(json)).ResourceSpans).Spans);
}
