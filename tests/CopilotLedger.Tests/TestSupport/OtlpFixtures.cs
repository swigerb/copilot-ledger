using System.Text;
using CopilotLedger.Otlp.Decoding;

namespace CopilotLedger.Tests.TestSupport;

public static class OtlpFixtures
{
    public const string PGold1Hex = "0ac5030a220a200a0c736572766963652e6e616d6512100a0e6769746875622d636f70696c6f7412f5020a100a0e6769746875622e636f70696c6f7412e0020a105b8efff798038103d269b633813fc60c1208eee19b7ec3c1b1742208eee19b7ec3c1b1732a0f63686174206d61692d636f64652d32300339c07e7b7387d4e9184140211fda87d4e9184a1f0a1567656e5f61692e6f7065726174696f6e2e6e616d6512060a04636861744a240a1467656e5f61692e726571756573742e6d6f64656c120c0a0a6d61692d636f64652d324a210a1967656e5f61692e75736167652e696e7075745f746f6b656e73120418aa86014a200a1a67656e5f61692e75736167652e6f75747075745f746f6b656e73120218054a2b0a2467656e5f61692e75736167652e63616368655f726561642e696e7075745f746f6b656e73120318801c4a240a176769746875622e636f70696c6f742e6e616e6f5f616975120921000000f8b295c4414a2c0a1e67656e5f61692e726573706f6e73652e66696e6973685f726561736f6e73120a2a080a060a0473746f707a008501010100001a2768747470733a2f2f6f70656e74656c656d657472792e696f2f736368656d61732f312e34352e30";

    public static byte[] PGold1 => Convert.FromHexString(PGold1Hex);

    public const string PGold1Json = """
        {
          "resourceSpans": [
            {
              "resource": { "attributes": [ { "key": "service.name", "value": { "stringValue": "github-copilot" } } ] },
              "scopeSpans": [
                {
                  "scope": { "name": "github.copilot" },
                  "spans": [
                    {
                      "traceId": "5B8EFFF798038103D269B633813FC60C",
                      "spanId": "EEE19B7EC3C1B174",
                      "parentSpanId": "EEE19B7EC3C1B173",
                      "name": "chat mai-code-2",
                      "kind": 3,
                      "startTimeUnixNano": "1795199604707000000",
                      "endTimeUnixNano": 1795199606429000000,
                      "attributes": [
                        { "key": "gen_ai.operation.name", "value": { "stringValue": "chat" } },
                        { "key": "gen_ai.request.model", "value": { "stringValue": "mai-code-2" } },
                        { "key": "gen_ai.usage.input_tokens", "value": { "intValue": "17194" } },
                        { "key": "gen_ai.usage.output_tokens", "value": { "intValue": 5 } },
                        { "key": "gen_ai.usage.cache_read.input_tokens", "value": { "intValue": "3584" } },
                        { "key": "github.copilot.nano_aiu", "value": { "doubleValue": 690710000.0 } },
                        { "key": "gen_ai.response.finish_reasons", "value": { "arrayValue": { "values": [ { "stringValue": "stop" } ] } } }
                      ],
                      "status": {},
                      "flags": 257
                    }
                  ]
                }
              ],
              "schemaUrl": "https://opentelemetry.io/schemas/1.45.0"
            }
          ]
        }
        """;

    public const string CopilotChatTraceJson = """
        {
          "resourceSpans": [
            {
              "resource": {
                "attributes": [
                  { "key": "service.name", "value": { "stringValue": "github-copilot" } },
                  { "key": "service.version", "value": { "stringValue": "1.0.112" } }
                ],
                "droppedAttributesCount": 0,
                "entityRefs": []
              },
              "scopeSpans": [
                {
                  "scope": { "name": "github.copilot", "version": "1.0.112", "attributes": [], "droppedAttributesCount": 0 },
                  "spans": [
                    {
                      "traceId": "5b8efff798038103d269b633813fc60c",
                      "spanId": "eee19b7ec3c1b174",
                      "traceState": "",
                      "parentSpanId": "eee19b7ec3c1b173",
                      "flags": 257,
                      "name": "chat mai-code-2",
                      "kind": 3,
                      "startTimeUnixNano": "1795199604707000000",
                      "endTimeUnixNano": "1795199606429000000",
                      "attributes": [
                        { "key": "gen_ai.operation.name", "value": { "stringValue": "chat" } },
                        { "key": "gen_ai.provider.name", "value": { "stringValue": "github" } },
                        { "key": "gen_ai.request.model", "value": { "stringValue": "mai-code-2" } },
                        { "key": "gen_ai.conversation.id", "value": { "stringValue": "0f8fad5b-d9cb-469f-a165-70867728950e" } },
                        { "key": "gen_ai.response.finish_reasons", "value": { "arrayValue": { "values": [ { "stringValue": "stop" } ] } } },
                        { "key": "gen_ai.usage.input_tokens", "value": { "intValue": "17194" } },
                        { "key": "gen_ai.usage.output_tokens", "value": { "intValue": "5" } },
                        { "key": "gen_ai.usage.cache_read.input_tokens", "value": { "intValue": "3584" } },
                        { "key": "gen_ai.response.model", "value": { "stringValue": "mai-code-2" } },
                        { "key": "github.copilot.nano_aiu", "value": { "doubleValue": 690710000.0 } }
                      ],
                      "droppedAttributesCount": 0,
                      "events": [],
                      "droppedEventsCount": 0,
                      "links": [],
                      "droppedLinksCount": 0,
                      "status": { "message": "", "code": 0 }
                    },
                    {
                      "traceId": "5b8efff798038103d269b633813fc60c",
                      "spanId": "eee19b7ec3c1b173",
                      "traceState": "",
                      "parentSpanId": "",
                      "flags": 257,
                      "name": "invoke_agent",
                      "kind": 1,
                      "startTimeUnixNano": "1795199604679000000",
                      "endTimeUnixNano": "1795199606444000000",
                      "attributes": [
                        { "key": "gen_ai.operation.name", "value": { "stringValue": "invoke_agent" } },
                        { "key": "gen_ai.provider.name", "value": { "stringValue": "github" } },
                        { "key": "gen_ai.conversation.id", "value": { "stringValue": "0f8fad5b-d9cb-469f-a165-70867728950e" } },
                        { "key": "github.copilot.agent.type", "value": { "stringValue": "builtin" } },
                        { "key": "gen_ai.usage.input_tokens", "value": { "intValue": "17194" } },
                        { "key": "gen_ai.usage.output_tokens", "value": { "intValue": "5" } },
                        { "key": "github.copilot.turn_count", "value": { "intValue": "1" } }
                      ],
                      "droppedAttributesCount": 0,
                      "events": [],
                      "droppedEventsCount": 0,
                      "links": [],
                      "droppedLinksCount": 0,
                      "status": { "message": "", "code": 0 }
                    }
                  ],
                  "schemaUrl": "https://opentelemetry.io/schemas/1.45.0"
                }
              ],
              "schemaUrl": ""
            }
          ]
        }
        """;

    public static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);

    public static void AssertPGold1(OtlpTraceRequest request)
    {
        Assert.Equal(1, request.SpanCount);
        OtlpResourceSpans resourceSpans = Assert.Single(request.ResourceSpans);
        OtlpAttribute resourceAttribute = Assert.Single(resourceSpans.ResourceAttributes);
        AssertAttribute(resourceAttribute, "service.name", OtlpValueKind.String, "github-copilot");
        OtlpSpan span = Assert.Single(resourceSpans.Spans);
        Assert.Equal("5b8efff798038103d269b633813fc60c", span.TraceId);
        Assert.Equal("eee19b7ec3c1b174", span.SpanId);
        Assert.Equal("eee19b7ec3c1b173", span.ParentSpanId);
        Assert.Equal("chat mai-code-2", span.Name);
        Assert.Equal(1795199604707000000UL, span.StartTimeUnixNano);
        Assert.Equal(1795199606429000000UL, span.EndTimeUnixNano);
        Assert.Collection(
            span.Attributes,
            attribute => AssertAttribute(attribute, "gen_ai.operation.name", OtlpValueKind.String, "chat"),
            attribute => AssertAttribute(attribute, "gen_ai.request.model", OtlpValueKind.String, "mai-code-2"),
            attribute => AssertAttribute(attribute, "gen_ai.usage.input_tokens", OtlpValueKind.Int, 17194),
            attribute => AssertAttribute(attribute, "gen_ai.usage.output_tokens", OtlpValueKind.Int, 5),
            attribute => AssertAttribute(attribute, "gen_ai.usage.cache_read.input_tokens", OtlpValueKind.Int, 3584),
            attribute => AssertAttribute(attribute, "github.copilot.nano_aiu", OtlpValueKind.Double),
            attribute => AssertAttribute(attribute, "gen_ai.response.finish_reasons", OtlpValueKind.Array));
    }

    public static void AssertAttribute(
        OtlpAttribute attribute,
        string key,
        OtlpValueKind kind,
        object? value = null)
    {
        Assert.Equal(key, attribute.Key);
        Assert.Equal(kind, attribute.Value.Kind);
        switch (kind)
        {
            case OtlpValueKind.String:
                Assert.Equal(value, attribute.Value.StringValue);
                break;
            case OtlpValueKind.Bool:
                Assert.Equal(value, attribute.Value.BoolValue);
                break;
            case OtlpValueKind.Int:
                Assert.Equal(Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture), attribute.Value.IntValue);
                break;
        }
    }

    public static OtlpProtoWriter PGold1Writer()
    {
        static OtlpProtoWriter KeyValue(string key, OtlpProtoWriter value) =>
            new OtlpProtoWriter().String(1, key).Message(2, value);

        OtlpProtoWriter span = new OtlpProtoWriter()
            .Bytes(1, Convert.FromHexString("5b8efff798038103d269b633813fc60c"))
            .Bytes(2, Convert.FromHexString("eee19b7ec3c1b174"))
            .Bytes(4, Convert.FromHexString("eee19b7ec3c1b173"))
            .String(5, "chat mai-code-2")
            .Varint(6, 3)
            .Fixed64(7, 1795199604707000000)
            .Fixed64(8, 1795199606429000000)
            .Message(9, KeyValue("gen_ai.operation.name", new OtlpProtoWriter().String(1, "chat")))
            .Message(9, KeyValue("gen_ai.request.model", new OtlpProtoWriter().String(1, "mai-code-2")))
            .Message(9, KeyValue("gen_ai.usage.input_tokens", new OtlpProtoWriter().Varint(3, 17194)))
            .Message(9, KeyValue("gen_ai.usage.output_tokens", new OtlpProtoWriter().Varint(3, 5)))
            .Message(9, KeyValue("gen_ai.usage.cache_read.input_tokens", new OtlpProtoWriter().Varint(3, 3584)))
            .Message(9, KeyValue("github.copilot.nano_aiu", new OtlpProtoWriter().Fixed64(4, 0x41C495B2F8000000)))
            .Message(9, KeyValue(
                "gen_ai.response.finish_reasons",
                new OtlpProtoWriter().Message(5, new OtlpProtoWriter().Message(1, new OtlpProtoWriter().String(1, "stop")))))
            .Message(15, new OtlpProtoWriter())
            .Fixed32(16, 257);
        OtlpProtoWriter scopeSpans = new OtlpProtoWriter()
            .Message(1, new OtlpProtoWriter().String(1, "github.copilot"))
            .Message(2, span);
        OtlpProtoWriter resource = new OtlpProtoWriter()
            .Message(1, KeyValue("service.name", new OtlpProtoWriter().String(1, "github-copilot")));
        OtlpProtoWriter resourceSpans = new OtlpProtoWriter()
            .Message(1, resource)
            .Message(2, scopeSpans)
            .String(3, "https://opentelemetry.io/schemas/1.45.0");
        return new OtlpProtoWriter().Message(1, resourceSpans);
    }
}
