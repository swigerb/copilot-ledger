using CopilotLedger.Otlp.Decoding;
using static CopilotLedger.Tests.TestSupport.OtlpRequests;

namespace CopilotLedger.Tests.TestSupport;

public static class BreakerTraces
{
    public const string TraceH = "0a000000000000000000000000000001";
    public const string ConversationH = "982f73ad-18f4-49ef-a1f9-70748fa013fb";
    public const string TraceV = "0b000000000000000000000000000001";
    public const string TraceW = "0c000000000000000000000000000001";

    public static OtlpTraceRequest TraceHRequest(int request) =>
        Request(
            [.. CopilotResource, Str("service.version", "1.0.112")],
            request switch
            {
                1 =>
                [
                    HChat("0a000000000000c1", 1795220210781000000, 1795220215181000000, 17299, 176, 6656),
                    HTool("0a000000000000a1", "apply_patch", 1795220215194000000, 1795220215351000000),
                ],
                2 =>
                [
                    HChat("0a000000000000c2", 1795220215368000000, 1795220217545000000, 17568, 44, 16896),
                    HTool("0a000000000000a2", "powershell", 1795220217552000000, 1795220219452000000, 1),
                    HChat("0a000000000000c3", 1795220219475000000, 1795220221084000000, 17681, 44, 16896),
                    HTool("0a000000000000a3", "apply_patch", 1795220221093000000, 1795220221233000000),
                ],
                3 =>
                [
                    HChat("0a000000000000c4", 1795220221248000000, 1795220222694000000, 17825, 44, 16896),
                    HTool("0a000000000000a4", "powershell", 1795220222710000000, 1795220223969000000, 1),
                    HChat("0a000000000000c5", 1795220223990000000, 1795220226844000000, 17938, 44, 16896),
                    HTool("0a000000000000a5", "apply_patch", 1795220226853000000, 1795220227044000000),
                ],
                4 =>
                [
                    HChat("0a000000000000c6", 1795220227064000000, 1795220228239000000, 18082, 44, 16896),
                    HTool("0a000000000000a6", "powershell", 1795220228246000000, 1795220229660000000, 1),
                    HChat("0a000000000000c7", 1795220229692000000, 1795220231435000000, 18195, 44, 0),
                    HTool("0a000000000000a7", "apply_patch", 1795220231440000000, 1795220231604000000),
                ],
                5 =>
                [
                    HChat("0a000000000000c8", 1795220231625000000, 1795220232986000000, 18339, 44, 17920),
                    HTool("0a000000000000a8", "powershell", 1795220232994000000, 1795220234202000000, 1),
                    HChat("0a000000000000c9", 1795220234220000000, 1795220235567000000, 18452, 44, 17920),
                    HTool("0a000000000000a9", "apply_patch", 1795220235578000000, 1795220235717000000),
                ],
                6 =>
                [
                    HChat("0a000000000000ca", 1795220235746000000, 1795220237251000000, 18596, 44, 17920),
                    HTool("0a000000000000aa", "powershell", 1795220237260000000, 1795220238525000000, 1),
                    HChat("0a000000000000cb", 1795220238545000000, 1795220240767000000, 18709, 44, 17920),
                    HTool("0a000000000000ab", "apply_patch", 1795220240772000000, 1795220240870000000),
                ],
                7 =>
                [
                    HChat("0a000000000000cc", 1795220240889000000, 1795220242864000000, 18853, 44, 6656),
                    HTool("0a000000000000ac", "powershell", 1795220242870000000, 1795220243998000000, 0),
                    HChat("0a000000000000cd", 1795220244019000000, 1795220245927000000, 18964, 57, 17920),
                    HTool("0a000000000000ad", "powershell", 1795220245934000000, 1795220246848000000, 0),
                    HChat("0a000000000000ce", 1795220246868000000, 1795220248402000000, 19101, 6, 17920),
                    Span(
                        TraceH,
                        "0a000000000000f0",
                        name: "invoke_agent",
                        startNs: 1795220210353000000,
                        endNs: 1795220248464000000,
                        attributes:
                        [
                            Str("gen_ai.operation.name", "invoke_agent"),
                            Str("gen_ai.provider.name", "github"),
                            Str("github.copilot.agent.type", "builtin"),
                            Str("gen_ai.conversation.id", ConversationH),
                        ]),
                ],
                _ => throw new ArgumentOutOfRangeException(nameof(request)),
            });

    public static OtlpTraceRequest TraceVRequest(string repoRoot, params int[] chats) =>
        Request(
            [.. CopilotResource, Str("process.working_directory", repoRoot)],
            [.. chats.Select(chat => VelocityChat(TraceV, 'b', chat, chat switch
            {
                1 => (1795255258000000000, 1795255260000000000),
                2 => (1795255318000000000, 1795255320000000000),
                3 => (1795255378000000000, 1795255380000000000),
                _ => throw new ArgumentOutOfRangeException(nameof(chats)),
            }))]);

    public static OtlpTraceRequest TraceWRequest(string repoRoot) =>
        Request(
            [.. CopilotResource, Str("process.working_directory", repoRoot)],
            VelocityChat(TraceW, 'c', 1, (1795255258000000000, 1795255260000000000)),
            VelocityChat(TraceW, 'c', 2, (1795255438000000000, 1795255440000000000)),
            VelocityChat(TraceW, 'c', 3, (1795255618000000000, 1795255620000000000)));

    private static OtlpSpan HChat(
        string spanId,
        ulong startNs,
        ulong endNs,
        long input,
        long output,
        long cacheRead)
    {
        List<OtlpAttribute> attributes =
        [
            Str("gen_ai.operation.name", "chat"),
            Str("gen_ai.provider.name", "github"),
            Str("gen_ai.request.model", "mai-code-2-flash"),
            Str("gen_ai.response.model", "mai-code-2-flash"),
            Str("gen_ai.conversation.id", ConversationH),
            Int("gen_ai.usage.input_tokens", input),
            Int("gen_ai.usage.output_tokens", output),
        ];
        if (cacheRead != 0)
        {
            attributes.Add(Int("gen_ai.usage.cache_read.input_tokens", cacheRead));
        }

        return Span(
            TraceH,
            spanId,
            "0a000000000000f0",
            "chat mai-code-2-flash",
            startNs,
            endNs,
            [.. attributes]);
    }

    private static OtlpSpan HTool(
        string spanId,
        string toolName,
        ulong startNs,
        ulong endNs,
        long? exitCode = null)
    {
        List<OtlpAttribute> attributes =
        [
            Str("gen_ai.operation.name", "execute_tool"),
            Str("gen_ai.tool.name", toolName),
            Str("gen_ai.conversation.id", ConversationH),
        ];
        if (exitCode is not null)
        {
            attributes.Add(Int("process.exit.code", exitCode.Value));
        }

        return Span(
            TraceH,
            spanId,
            "0a000000000000f0",
            toolName == "powershell"
                ? "execute_tool powershell pwsh.exe"
                : "execute_tool apply_patch",
            startNs,
            endNs,
            [.. attributes]);
    }

    private static OtlpSpan VelocityChat(
        string traceId,
        char tracePrefix,
        int chat,
        (ulong Start, ulong End) times)
    {
        string spanId = $"0{tracePrefix}000000000000c{chat}";
        return Span(
            traceId,
            spanId,
            name: "chat claude-opus-5.5",
            startNs: times.Start,
            endNs: times.End,
            attributes:
            [
                Str("gen_ai.operation.name", "chat"),
                Str("gen_ai.provider.name", "github"),
                Str("gen_ai.request.model", "claude-opus-5.5"),
                Str("gen_ai.conversation.id", $"conv-{tracePrefix switch { 'b' => 'v', 'c' => 'w', _ => 'x' }}"),
                Int("gen_ai.usage.input_tokens", 100000),
                Int("gen_ai.usage.output_tokens", 10000),
            ]);
    }
}
