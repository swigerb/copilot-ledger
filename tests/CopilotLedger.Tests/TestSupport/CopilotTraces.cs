using CopilotLedger.Otlp.Decoding;
using static CopilotLedger.Tests.TestSupport.OtlpRequests;

namespace CopilotLedger.Tests.TestSupport;

public static class CopilotTraces
{
    public static OtlpTraceRequest TraceBA() => Request(
        CopilotResourceAnd(Str("service.version", "1.0.112")),
        ChatB(
            "00f067aa0ba902bc",
            "00f067aa0ba902ba",
            "execute_tool powershell",
            1795199714402000000,
            1795199715770000000,
            Str("gen_ai.operation.name", "execute_tool"),
            Str("gen_ai.tool.name", "powershell"),
            Str("gen_ai.agent.name", "explore")),
        ChatB(
            "00f067aa0ba902bb",
            "00f067aa0ba902ba",
            "chat gpt-6-luna",
            1795199712404000000,
            1795199714391000000,
            Str("gen_ai.operation.name", "chat"),
            Str("gen_ai.provider.name", "github"),
            Str("gen_ai.request.model", "gpt-6-luna"),
            Str("gen_ai.response.model", "gpt-6-luna"),
            Int("gen_ai.usage.input_tokens", 3665),
            Int("gen_ai.usage.output_tokens", 43),
            Int("gen_ai.usage.cache_write.input_tokens", 3662)),
        ChatB(
            "00f067aa0ba902bd",
            "00f067aa0ba902ba",
            "chat gpt-6-luna",
            1795199715775000000,
            1795199716540000000,
            Str("gen_ai.operation.name", "chat"),
            Str("gen_ai.provider.name", "github"),
            Str("gen_ai.request.model", "gpt-6-luna"),
            Str("gen_ai.response.model", "gpt-6-luna"),
            Int("gen_ai.usage.input_tokens", 3764),
            Int("gen_ai.usage.output_tokens", 5),
            Int("gen_ai.usage.cache_read.input_tokens", 3662),
            Int("gen_ai.usage.cache_write.input_tokens", 99)),
        ChatB(
            "00f067aa0ba902ba",
            "00f067aa0ba902b9",
            "invoke_agent explore",
            1795199712280000000,
            1795199716548000000,
            Str("gen_ai.operation.name", "invoke_agent"),
            Str("gen_ai.provider.name", "github"),
            Str("gen_ai.agent.name", "explore")),
        ChatB(
            "00f067aa0ba902b9",
            "00f067aa0ba902b7",
            "execute_tool task",
            1795199712265000000,
            1795199716550000000,
            Str("gen_ai.operation.name", "execute_tool"),
            Str("gen_ai.tool.name", "task")),
        ChatB(
            "00f067aa0ba902b8",
            "00f067aa0ba902b7",
            "chat mai-code-2",
            1795199709019000000,
            1795199712254000000,
            Str("gen_ai.operation.name", "chat"),
            Str("gen_ai.provider.name", "github"),
            Str("gen_ai.request.model", "mai-code-2"),
            Str("gen_ai.response.model", "mai-code-2"),
            Int("gen_ai.usage.input_tokens", 17194),
            Int("gen_ai.usage.output_tokens", 99),
            Int("gen_ai.usage.cache_read.input_tokens", 15872)));

    public static OtlpTraceRequest TraceBB() => Request(
        CopilotResourceAnd(Str("service.version", "1.0.112")),
        ChatB(
            "00f067aa0ba902be",
            "00f067aa0ba902b7",
            "chat mai-code-2",
            1795199716568000000,
            1795199720284000000,
            Str("gen_ai.operation.name", "chat"),
            Str("gen_ai.provider.name", "github"),
            Str("gen_ai.request.model", "mai-code-2"),
            Str("gen_ai.response.model", "mai-code-2"),
            Int("gen_ai.usage.input_tokens", 17333),
            Int("gen_ai.usage.output_tokens", 5),
            Int("gen_ai.usage.cache_read.input_tokens", 3584)),
        ChatB(
            "00f067aa0ba902b7",
            null,
            "invoke_agent",
            1795199708999000000,
            1795199720318000000,
            Str("gen_ai.operation.name", "invoke_agent"),
            Str("gen_ai.provider.name", "github"),
            Str("github.copilot.agent.type", "builtin")));

    public static OtlpTraceRequest TraceD1() => Request(
        [Str("service.name", "copilot-chat")],
        Span(
            traceId: "a3ce929d0e0e47364bf92f3577b34da6",
            spanId: "c0ffee0000000002",
            parentSpanId: "c0ffee0000000001",
            startNs: 1795200001000000000,
            endNs: 1795200002000000000,
            attributes:
            [
                Str("gen_ai.operation.name", "chat"),
                Str("gen_ai.provider.name", "github"),
                Str("gen_ai.request.model", "claude-opus-5.5"),
                Int("gen_ai.usage.input_tokens", 100000),
                Int("gen_ai.usage.output_tokens", 10000),
                Int("gen_ai.usage.cache_creation.input_tokens", 0),
            ]));

    public static OtlpTraceRequest TraceD2() => Request(
        [Str("service.name", "copilot-chat")],
        Span(
            traceId: "a3ce929d0e0e47364bf92f3577b34da6",
            spanId: "c0ffee0000000001",
            startNs: 1795200000000000000,
            endNs: 1795200010000000000,
            attributes:
            [
                Str("gen_ai.operation.name", "invoke_agent"),
                Str("gen_ai.agent.name", "GitHub Copilot Chat"),
                Str("gen_ai.conversation.id", "vscode-session-1"),
                Str("github.copilot.git.repository", "https://user:s3cr3t@github.com/contoso/webapp.git"),
                Str("github.copilot.git.branch", "feature/login"),
                Str("github.copilot.git.commit_sha", "0123456789ABCDEF0123456789ABCDEF01234567"),
            ]));

    public static OtlpTraceRequest TraceE(IReadOnlyList<OtlpAttribute>? extraResource = null)
    {
        List<OtlpAttribute> resource = [.. CopilotResource];
        if (extraResource is not null)
        {
            resource.AddRange(extraResource);
        }

        return Request(
            resource,
            Span(
                traceId: "e4c5e6a7b8c9d0e1f2a3b4c5d6e7f809",
                spanId: "e4c5e6a7b8c9d0e1",
                startNs: 1795200100000000000,
                endNs: 1795200102000000000,
                attributes:
                [
                    Str("gen_ai.operation.name", "chat"),
                    Str("gen_ai.provider.name", "github"),
                    Str("gen_ai.response.model", "claude-opus-5.5"),
                    Str("gen_ai.conversation.id", "d9b2d63d-a233-4123-847a-5a1f3c8e6e1f"),
                    Int("gen_ai.usage.input_tokens", 1200000),
                    Int("gen_ai.usage.output_tokens", 50000),
                    Int("gen_ai.usage.cache_read.input_tokens", 800000),
                    Int("gen_ai.usage.cache_write.input_tokens", 100000),
                ]));
    }

    public static OtlpTraceRequest TraceF() => Request(
        CopilotResourceAnd(Str("service.version", "1.0.112")),
        Span(
            traceId: "c0de0000000000000000000000000f01",
            spanId: "0f00000000000002",
            parentSpanId: "0f00000000000001",
            name: "chat mai-code-2-flash",
            startNs: 1795207524382000000,
            endNs: 1795207526444000000,
            attributes:
            [
                Str("gen_ai.operation.name", "chat"),
                Str("gen_ai.provider.name", "github"),
                Str("gen_ai.request.model", "mai-code-2-flash"),
                Str("gen_ai.response.model", "mai-code-2-flash"),
                Str("gen_ai.conversation.id", "f1d5c3a2-9b8e-4d7c-a6f5-e4d3c2b1a0f1"),
                Int("gen_ai.usage.input_tokens", 4734),
                Int("gen_ai.usage.output_tokens", 17),
            ]),
        Span(
            traceId: "c0de0000000000000000000000000f01",
            spanId: "0f00000000000001",
            name: "invoke_agent Ledger Probe Display",
            startNs: 1795207524370000000,
            endNs: 1795207526476000000,
            attributes:
            [
                Str("gen_ai.operation.name", "invoke_agent"),
                Str("gen_ai.provider.name", "github"),
                Str("gen_ai.agent.name", "Ledger Probe Display"),
                Str("gen_ai.conversation.id", "f1d5c3a2-9b8e-4d7c-a6f5-e4d3c2b1a0f1"),
                Str("github.copilot.agent.type", "custom"),
            ]));

    public static OtlpTraceRequest TraceG() => Request(
        CopilotResourceAnd(Str("service.version", "1.0.112")),
        CustomChatG(
            "0f00000000000015",
            "0f00000000000014",
            1795207544429000000,
            1795207546581000000,
            954,
            5),
        CustomAgentG(),
        Span(
            traceId: "c0de0000000000000000000000000f02",
            spanId: "0f00000000000013",
            parentSpanId: "0f00000000000011",
            name: "execute_tool task",
            startNs: 1795207544328000000,
            endNs: 1795207546595000000,
            attributes:
            [
                Str("gen_ai.operation.name", "execute_tool"),
                Str("gen_ai.tool.name", "task"),
                Str("gen_ai.transfer.target.name", "Ledger Probe Display"),
                ConversationG(),
            ]),
        CustomChatG(
            "0f00000000000012",
            "0f00000000000011",
            1795207542071000000,
            1795207544319000000,
            5560,
            42),
        CustomChatG(
            "0f00000000000016",
            "0f00000000000011",
            1795207546608000000,
            1795207549789000000,
            5642,
            5),
        Span(
            traceId: "c0de0000000000000000000000000f02",
            spanId: "0f00000000000011",
            name: "invoke_agent",
            startNs: 1795207542057000000,
            endNs: 1795207549819000000,
            attributes:
            [
                Str("gen_ai.operation.name", "invoke_agent"),
                Str("gen_ai.provider.name", "github"),
                ConversationG(),
                Str("github.copilot.agent.type", "builtin"),
            ]));

    private static OtlpSpan ChatB(
        string spanId,
        string? parentSpanId,
        string name,
        ulong startNs,
        ulong endNs,
        params OtlpAttribute[] attributes) => Span(
            traceId: "4bf92f3577b34da6a3ce929d0e0e4736",
            spanId: spanId,
            parentSpanId: parentSpanId,
            name: name,
            startNs: startNs,
            endNs: endNs,
            attributes: [.. attributes, Str(
                "gen_ai.conversation.id",
                "7c9e6679-7425-40de-944b-e07fc1f90ae7")]);

    private static OtlpSpan CustomChatG(
        string spanId,
        string parentSpanId,
        ulong startNs,
        ulong endNs,
        long input,
        long output) => Span(
            traceId: "c0de0000000000000000000000000f02",
            spanId: spanId,
            parentSpanId: parentSpanId,
            name: "chat mai-code-2-flash",
            startNs: startNs,
            endNs: endNs,
            attributes:
            [
                Str("gen_ai.operation.name", "chat"),
                Str("gen_ai.provider.name", "github"),
                Str("gen_ai.request.model", "mai-code-2-flash"),
                Str("gen_ai.response.model", "mai-code-2-flash"),
                ConversationG(),
                Int("gen_ai.usage.input_tokens", input),
                Int("gen_ai.usage.output_tokens", output),
            ]);

    private static OtlpSpan CustomAgentG() => Span(
        traceId: "c0de0000000000000000000000000f02",
        spanId: "0f00000000000014",
        parentSpanId: "0f00000000000013",
        name: "invoke_agent Ledger Probe Display",
        startNs: 1795207544337000000,
        endNs: 1795207546589000000,
        attributes:
        [
            Str("gen_ai.operation.name", "invoke_agent"),
            Str("gen_ai.provider.name", "github"),
            Str("gen_ai.agent.name", "Ledger Probe Display"),
            ConversationG(),
            Str("github.copilot.agent.type", "custom"),
        ]);

    private static OtlpAttribute ConversationG() =>
        Str("gen_ai.conversation.id", "f2d5c3a2-9b8e-4d7c-a6f5-e4d3c2b1a0f2");

    private static IReadOnlyList<OtlpAttribute> CopilotResourceAnd(
        params OtlpAttribute[] additional) =>
        [.. CopilotResource, .. additional];
}
